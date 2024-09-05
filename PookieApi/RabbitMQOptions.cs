
namespace PookieApi;
public class RabbitMQOptions
{
    public const string Position = "RabbitMQ"; 
    public string HostName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}