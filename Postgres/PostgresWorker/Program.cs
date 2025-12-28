using System.Data;
using Npgsql;
using PostgresWorker.Job;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddScoped<JobRepository>();
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var cs = sp.GetRequiredService<IConfiguration>()
        .GetConnectionString("Postgres");

    return new NpgsqlConnection(cs);
});

builder.Services.AddHostedService<JobProcessorService>();

var host = builder.Build();

host.Run();
