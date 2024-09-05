
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PookieApi;
using PookieApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Postgres connection string not defined."))
    .AddRabbitMQ(rabbitConnectionString: "amqp://rabbitmq-user:password@rabbitmq:5672/" ?? throw new InvalidOperationException("Rabbit Mq configuration not set."));

builder.Services.Configure<RabbitMQOptions>(builder.Configuration.GetSection("RabbitMQ"));

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

app.MapPost("/api/messages/send", (RabbitMqService rabbitMqService, [FromBody] string message) =>
{
    rabbitMqService.Publish(message);
    return Results.Ok("Message sent to RabbitMQ");
})
.WithName("SendMessage")
.WithOpenApi();

app.MapGet("/api/messages", async (ApplicationDbContext dbContext) =>
{
    var messages = await dbContext.Messages.ToListAsync();
    return Results.Ok(messages);
})
.WithName("GetMessages")
.WithOpenApi();

app.MapHealthChecks("/health");

app.Run();
