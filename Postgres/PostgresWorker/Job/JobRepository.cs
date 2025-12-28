using System.Data;
using Dapper;

namespace PostgresWorker.Job;

public sealed class JobRepository
{
    private readonly IDbConnection _connection;

    public JobRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<Job?> TryLeaseJobAsync(Guid workerId, CancellationToken ct)
    {
        const string sql = """
                           UPDATE jobs
                           SET
                               status = 'leased',
                               leased_by = @workerId,
                               lease_until = now() + interval '2 minutes',
                               heartbeat_at = now(),
                               updated_at = now()
                           WHERE id = (
                               SELECT id
                               FROM jobs
                               -- TODO: What about failed jobs?
                               -- Jobs are marked as 'failed' if processing fails
                               WHERE status = 'pending'
                                  OR (status = 'leased' AND lease_until < now())
                               ORDER BY created_at
                               LIMIT 1
                               FOR UPDATE SKIP LOCKED
                           )
                           RETURNING id, payload;
                           """;

        return await _connection.QuerySingleOrDefaultAsync<Job>(sql, new { workerId });
    }

    public Task HeartbeatAsync(Guid jobId, CancellationToken ct)
        => _connection.ExecuteAsync(
            "UPDATE jobs SET lease_until = now() + interval '2 minutes', heartbeat_at = now() WHERE id = @jobId",
            new { jobId });

    public Task MarkCompletedAsync(Guid jobId, CancellationToken ct)
        => _connection.ExecuteAsync(
            "UPDATE jobs SET status = 'completed', updated_at = now() WHERE id = @jobId",
            new { jobId });

    public Task MarkFailedAsync(Guid jobId, CancellationToken ct)
        => _connection.ExecuteAsync(
            "UPDATE jobs SET status = 'failed', updated_at = now() WHERE id = @jobId",
            new { jobId });
}
