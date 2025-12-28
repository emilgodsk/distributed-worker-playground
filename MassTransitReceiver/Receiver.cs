using MassTransit;
using MassTransitContracts;

public class Receiver : IConsumer<Message>
{
    public async Task Consume(ConsumeContext<Message> context)
    {
        try
        {
            var message = context.Message.Text;
            var waitTime = int.Parse(message.Split(';')[1]);
            var messageNumber = int.Parse(message.Split(';')[0]);
            Console.WriteLine($" [{messageNumber}] Received message with wait time of {waitTime}");

            Console.WriteLine($" [{messageNumber}] Waiting... ");
            var waitedTime = 0;
            while (waitedTime < waitTime)
            {
                waitedTime += 5000;
                await Task.Delay(5000); // Simulate a long processing task
                Console.WriteLine($" [{messageNumber}] Waiting... so far {waitedTime} ms");
            }

            Console.WriteLine($" [{messageNumber}] Waited {waitedTime} ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Error processing message: {ex.Message}");
            throw;
        }
    }
}
