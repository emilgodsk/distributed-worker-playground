using RabbitMQ.Client;
using System.Text;
using RabbitMQ.Client.Events;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    ConsumerDispatchConcurrency = 2,
    AutomaticRecoveryEnabled = true
};
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

await channel.QueueDeclareAsync(
    queue: "hello",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null);

Console.WriteLine(" Press [enter] to start receiving.");
Console.ReadLine();

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.RegisteredAsync += async (ch, ea) =>
{
    Console.WriteLine(" Consumer registered.");
    await Task.CompletedTask;
};
consumer.ShutdownAsync += async (ch, ea) =>
{
    Console.WriteLine(" Consumer shutdown.");
    await Task.CompletedTask;
};
consumer.UnregisteredAsync += async (ch, ea) =>
{
    Console.WriteLine(" Consumer unregistered.");
    await Task.CompletedTask;
};

consumer.ReceivedAsync += async (ch, ea) =>
{
    try
    {
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        var waitTime = int.Parse(message.Split(';')[1]);
        var messageNumber = int.Parse(message.Split(';')[0]);
        Console.WriteLine($" [{messageNumber}] Received message with wait time of {waitTime}");

        Console.WriteLine($" [{messageNumber}] Waiting... ");
        var waitedTime = 0;
        while (waitedTime < waitTime)
        {
            waitedTime += 5000;
            Task.Delay(5000).Wait(); // Simulate a long processing task
            Console.WriteLine($" [{messageNumber}] Waiting... so far {waitedTime} ms");
        }

        Console.WriteLine($" [{messageNumber}] Waited {waitedTime} ms");

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error processing message: {ex.Message}");
        throw;
    }
};

await channel.BasicConsumeAsync("hello", autoAck: false, consumer: consumer);

var exit = false;
do
{
    Console.WriteLine(" Press [x] to exit.");
    var input = Console.ReadLine();
    if (input?.ToLower() == "x")
    {
        exit = true;
    }
} while (!exit);
