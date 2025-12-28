using System.Threading.Channels;

namespace PostgresWorker.Job;

public sealed class JobProcessorService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<JobProcessorService> _logger;

    // Job handoff channel
    private readonly Channel<Job> _jobChannel = Channel.CreateUnbounded<Job>();

    // Worker-ready channel
    private readonly Channel<Guid> _workerReadyChannel = Channel.CreateBounded<Guid>(MaxWorkers);

    // Keep max workers configurable - consider the amount of open connections this will potentially create
    // As well as how many concurrent jobs your system can handle
    private const int MaxWorkers = 4;

    // Unique processor ID for this instance
    private readonly string _processorId = Guid.NewGuid().ToString();

    private static readonly TimeSpan DispatcherPollDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatPulseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WorkerRestartDelay = TimeSpan.FromSeconds(5);

    public JobProcessorService(IServiceProvider services, ILogger<JobProcessorService> logger)
    {
        _services = services;
        _logger = logger;

        _logger.LogInformation(
            "JobProcessorService initialized with {WorkerCount} workers",
            MaxWorkers);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobProcessorService starting");

        try
        {
            var dispatcher = Task.Run(
                () => DispatcherLoop(stoppingToken),
                stoppingToken);

            var workerSupervisors = Enumerable.Range(0, MaxWorkers)
                .Select(_ => Task.Run(
                    () => WorkerSupervisorLoop(Guid.NewGuid(), stoppingToken),
                    stoppingToken))
                .ToArray();

            await Task.WhenAll(workerSupervisors.Append(dispatcher));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("JobProcessorService stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "JobProcessorService crashed");
            throw;
        }
    }

    private async Task DispatcherLoop(CancellationToken ct)
    {
        _logger.LogInformation("Dispatcher started (processorId={ProcessorId})", _processorId);

        while (!ct.IsCancellationRequested)
        {
            // 1️⃣ Wait for a ready worker
            var workerId = await _workerReadyChannel.Reader.ReadAsync(ct);

            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<JobRepository>();

            Job? job = null;

            _logger.LogDebug("Trying to lease job for worker {WorkerId}", workerId);

            try
            {
                // 2️⃣ Lease ONE job from DB
                job = await repo.TryLeaseJobAsync(workerId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lease job");
                // Return worker slot back
                await _workerReadyChannel.Writer.WriteAsync(workerId, ct);
                await Task.Delay(DispatcherPollDelay, ct);
                continue;
            }

            if (job == null)
            {
                _logger.LogDebug("No jobs available, returning worker token");
                await _workerReadyChannel.Writer.WriteAsync(workerId, ct);
                await Task.Delay(DispatcherPollDelay, ct);
                continue;
            }

            _logger.LogInformation("Leased job {JobId} for worker {WorkerId}", job.Id, workerId);

            // 3️⃣ Dispatch job to worker
            await _jobChannel.Writer.WriteAsync(job, ct);
        }

        _logger.LogInformation("Dispatcher stopped");
    }

    private async Task WorkerSupervisorLoop(Guid workerId, CancellationToken ct)
    {
        _logger.LogInformation("Worker supervisor {WorkerId} started", workerId);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await WorkerLoop(workerId, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Worker {WorkerId} stopping due to cancellation",
                    workerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Worker {WorkerId} crashed, restarting after delay",
                    workerId);

                // Restart the worker after a short delay
                await Task.Delay(WorkerRestartDelay, ct);
            }
        }

        _logger.LogInformation("Worker supervisor {WorkerId} stopped", workerId);
    }

    private async Task WorkerLoop(Guid workerId, CancellationToken ct)
    {
        _logger.LogInformation("Worker {WorkerId} started", workerId);

        while (!ct.IsCancellationRequested)
        {
            // Signal readiness to dispatcher
            await _workerReadyChannel.Writer.WriteAsync(workerId, ct);

            // Wait for a job from dispatcher
            var job = await _jobChannel.Reader.ReadAsync(ct);

            using var _ = _logger.BeginScope(new Dictionary<string, object>
            {
                ["JobId"] = job.Id,
                ["WorkerId"] = workerId
            });

            _logger.LogInformation(
                "Worker {WorkerId} started job {JobId}",
                workerId,
                job.Id);

            using var jobServiceScope = _services.CreateScope();
            var jobRepository = jobServiceScope.ServiceProvider.GetRequiredService<JobRepository>();

            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = StartHeartbeat(job.Id, jobRepository, heartbeatCts.Token);

            var workerStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await ProcessJobAsync(job, ct);

                await jobRepository.MarkCompletedAsync(job.Id, ct);

                _logger.LogInformation(
                    "Worker {WorkerId} completed job {JobId} in {DurationMs}ms",
                    workerId,
                    job.Id,
                    workerStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Worker {WorkerId} failed job {JobId} after {DurationMs}ms",
                    workerId,
                    job.Id,
                    workerStopwatch.ElapsedMilliseconds);

                await jobRepository.MarkFailedAsync(job.Id, ct);
            }
            finally
            {
                heartbeatCts.Cancel();
                await heartbeatTask;
            }
        }

        _logger.LogInformation("Worker {WorkerId} stopped", workerId);
    }

    private Task StartHeartbeat(Guid jobId, JobRepository repo, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            _logger.LogDebug("Heartbeat started for job {JobId}", jobId);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatPulseDelay, ct);
                    await repo.HeartbeatAsync(jobId, ct);

                    _logger.LogDebug(
                        "Heartbeat renewed for job {JobId}",
                        jobId);
                }
            }
            catch (OperationCanceledException)
            {
                // expected cancellation once a job finishes
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Heartbeat failed for job {JobId}",
                    jobId);
                throw;
            }
            finally
            {
                _logger.LogDebug("Heartbeat stopped for job {JobId}", jobId);
            }
        }, ct);
    }

    private async Task ProcessJobAsync(Job job, CancellationToken ct)
    {
        // Simulate long-running work
        await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 15)), ct);
        _logger.LogInformation("Processed job {JobId}", job.Id);
    }
}
