using MassTransit;
using MassTransitContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class SenderService : BackgroundService
{
    private readonly ISendEndpointProvider _sendEndpointProvider;

    private readonly ILogger<SenderService> _logger;

    public SenderService(ISendEndpointProvider sendEndpointProvider, ILogger<SenderService> logger)
    {
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Console input service started.");

        var messageCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            // ReadLine blocks, so run it on a background thread
            Console.WriteLine(" Press [enter] to send message. [x] to exit.");
            var input = await Task.Run(Console.ReadLine, stoppingToken);
            if (input is null)
            {
                continue;
            }

            if (input?.ToLower() == "x")
            {
                break;
            }

            var parsedInt = int.TryParse(input, out var waitTime) ? waitTime : 0;
            SendMessage(parsedInt);
        }

        return;

        async void SendMessage(int waitTime)
        {
            var message = $"{messageCount};{waitTime}";

            var endpoint = await _sendEndpointProvider
                .GetSendEndpoint(new Uri("queue:hello"));

            await endpoint.Send(new Message { Text = message }, stoppingToken);

            Console.WriteLine($" [{messageCount}] Sent message of {message}");
            messageCount++;
        };
    }
}
