// See https://aka.ms/new-console-template for more information

using MassTransit;
using MassTransitContracts;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<Receiver>();
    x.UsingRabbitMq((context, cfg) =>
    {

        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);
        cfg.ReceiveEndpoint("hello", e =>
        {
            e.PrefetchCount = 2;
            e.ConfigureConsumer<Receiver>(context);
        });
    });
});

var app = builder.Build();

app.Run();
