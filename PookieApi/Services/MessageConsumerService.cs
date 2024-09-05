using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PookieApi.Services
{
    public class MessageConsumerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MessageConsumerService> _logger;
        private readonly RabbitMQOptions _rabbitMqOptions;
        private IConnection? _connection;
        private IModel? _channel;

        public MessageConsumerService(IOptions<RabbitMQOptions> rabbitMqOptions, IServiceProvider serviceProvider, ILogger<MessageConsumerService> logger)
        {
            _rabbitMqOptions = rabbitMqOptions.Value;
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            InitializeRabbitMq();
        }

        private void InitializeRabbitMq()
        {
            var factory = new ConnectionFactory
            {
                HostName = _rabbitMqOptions.HostName,
                UserName = _rabbitMqOptions.UserName,
                Password = _rabbitMqOptions.Password
            };

            try
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();
                _channel.QueueDeclare(queue: "messages",
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize RabbitMQ connection: {ex.Message}");
                throw;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                StartConsuming(stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken); // Keeps the service running
            }
            catch (OperationCanceledException)
            {
                // Task was canceled, expected during service shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unhandled exception in ExecuteAsync: {ex.Message}");
            }
        }

        private void StartConsuming(CancellationToken cancellationToken)
        {
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var messageContent = Encoding.UTF8.GetString(body);

                try
                {
                    bool processedSuccessfully = await ProcessMessageAsync(messageContent);

                    if (processedSuccessfully)
                    {
                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        _channel.BasicReject(deliveryTag: ea.DeliveryTag, requeue: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception occurred while processing message: {ex.Message}");
                    _channel.BasicReject(deliveryTag: ea.DeliveryTag, requeue: true);
                }
            };

            _channel.BasicConsume(queue: "messages",
                                 autoAck: false,
                                 consumer: consumer);

            _logger.LogInformation("Started consuming messages from RabbitMQ.");
        }

        private async Task<bool> ProcessMessageAsync(string message)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var messageEntity = new Message
                    {
                        Content = message,
                        CreatedAt = DateTime.UtcNow
                    };

                    dbContext.Messages.Add(messageEntity);
                    await dbContext.SaveChangesAsync();

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing message: {ex.Message}");
                return false;
            }
        }

        public override void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            base.Dispose();
        }
    }
}
