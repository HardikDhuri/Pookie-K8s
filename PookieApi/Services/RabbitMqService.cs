using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;

namespace PookieApi.Services;
public class RabbitMqService(IOptionsMonitor<RabbitMQOptions> options)
{
    private readonly RabbitMQOptions _rabbitMqSetting = options.CurrentValue;

    public void Publish(string message)
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSetting.HostName,
            UserName = _rabbitMqSetting.UserName,
            Password = _rabbitMqSetting.Password
        };

        using var connection = factory.CreateConnection();
        
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "messages",
                             durable: false,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        var body = Encoding.UTF8.GetBytes(message);

        channel.BasicPublish(exchange: "",
                             routingKey: "messages",
                             basicProperties: null,
                             body: body);
    }
}
