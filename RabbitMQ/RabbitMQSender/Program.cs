using RabbitMQ.Client;
using System.Text;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    ConsumerDispatchConcurrency = 2,
    AutomaticRecoveryEnabled = true
};
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

var messageCount = 0;
await channel.QueueDeclareAsync(
    queue: "hello",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null);

var exit = false;
do
{
    Console.WriteLine(" Press [enter] to send message. [x] to exit.");
    var input = Console.ReadLine();
    if (input?.ToLower() == "x")
    {
        exit = true;
    }
    else
    {
        var parsedInt = int.TryParse(input, out var waitTime) ? waitTime : 0;
        SendMessage(parsedInt);
    }
} while (!exit);

return;

async void SendMessage(int waitTime)
{
    var message = $"{messageCount};{waitTime}";
    var body = Encoding.UTF8.GetBytes(message);
    await channel.BasicPublishAsync(exchange: string.Empty, routingKey: "hello", body: body);
    Console.WriteLine($" [{messageCount}] Sent message of {message}");
    messageCount++;
};
