
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PookieApi;
using PookieApi.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var resource = ResourceBuilder.CreateDefault().AddService("PookeApi");


// Configure Logging
LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", typeof(Program).Assembly.FullName!);

bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

if (useOtlpExporter)
{
    loggerConfiguration.WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        string[] headers = builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"]?.Split(',') ?? [];

        foreach (string header in headers)
        {
            (string key, string value) = header.Split('=') switch
            {
            [string k, string v] => (k, v),
                var v => throw new InvalidOperationException($"Invalid header format {v}")
            };

            options.Headers.Add(key, value);
        }

        options.ResourceAttributes.Add("service.name", "PookieApi");
    });
}

builder.Logging
    .ClearProviders()
    .AddSerilog(loggerConfiguration.CreateLogger());

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resource);
});

// Configure Telemetry
var otelBuilder = builder.Services.AddOpenTelemetry()
    .WithMetrics(metricsBuilder => metricsBuilder
        .AddMeter("PookieApi")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithTracing(traceBuilder => traceBuilder
        .AddSource("PookieApi", "1.0.0")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
    );

otelBuilder.ConfigureResource((resource) =>
{
    resource.AddAttributes(
    [
        new("service.name", "PookieApi"),
        new("service.version", "1.0.0")
    ]);
});

if (useOtlpExporter)
{
    otelBuilder.UseOtlpExporter();
}

Activity.DefaultIdFormat = ActivityIdFormat.W3C;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var mqOptions = new RabbitMQOptions();
builder.Configuration.GetSection(RabbitMQOptions.Position).Bind(mqOptions);

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Postgres connection string not defined."))
    .AddRabbitMQ(rabbitConnectionString: $"amqp://{mqOptions.UserName}:{mqOptions.Password}@{mqOptions.HostName}:5672/" ?? throw new InvalidOperationException("Rabbit Mq configuration not set."));

builder.Services.AddOptions<RabbitMQOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMQOptions.Position));

builder.Services.AddSingleton<RabbitMqService>();
builder.Services.AddHostedService<MessageConsumerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pookie API V1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();

app.MapPost("/api/messages/send", (ILogger<Program> logger, RabbitMqService rabbitMqService, HttpContext context, [FromBody] string message) =>
{
    logger.LogInformation("Request to store a message received: {RequestId}", Activity.Current?.TraceId.ToString() ?? string.Empty);

    context.Request.Headers.Append("Request-Id", Activity.Current?.TraceId.ToString() ?? string.Empty);

    rabbitMqService.Publish(message);

    logger.LogInformation("Message published to Queue");

    return Results.Ok("Message sent to RabbitMQ");
})
.WithName("SendMessage")
.WithOpenApi();

app.MapGet("/api/messages", async (ILogger<Program> logger, ApplicationDbContext dbContext, HttpContext context) =>
{
    logger.LogInformation("Request to get all messages received: {RequestId}", Activity.Current?.TraceId.ToString() ?? string.Empty);

    context.Request.Headers.Append("Request-Id", Activity.Current?.TraceId.ToString() ?? string.Empty);

    var messages = await dbContext.Messages.ToListAsync();

    logger.LogInformation("Messages retreived and sending them to user");

    return Results.Ok(messages);
})
.WithName("GetMessages")
.WithOpenApi();

app.MapHealthChecks("/health");

app.Run();
