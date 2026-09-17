using System.Text;
using CoreFid.Auditoria.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CoreFid.Auditoria.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string Seccion = "RabbitMq";

    public bool Habilitado { get; set; }
    public string Host { get; set; } = "localhost";
    public int Puerto { get; set; } = 5672;
    public string Usuario { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "corefid.auditoria";
}

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task Publicar(string tipo, string payload, Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var canal = ObtenerCanal();
            var propiedades = canal.CreateBasicProperties();
            propiedades.Persistent = true;
            propiedades.ContentType = "application/json";
            propiedades.MessageId = eventId.ToString();
            propiedades.Type = tipo;

            canal.BasicPublish(_options.Exchange, tipo, mandatory: false, propiedades, Encoding.UTF8.GetBytes(payload));
            canal.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        }

        return Task.CompletedTask;
    }

    private IModel ObtenerCanal()
    {
        if (_channel is { IsOpen: true }) return _channel;

        _channel?.Dispose();
        _connection?.Dispose();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Puerto,
            UserName = _options.Usuario,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection("corefid-auditoria-outbox");
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ConfirmSelect();
        _logger.LogInformation("Conexión RabbitMQ establecida contra {Host}:{Puerto} exchange {Exchange}",
            _options.Host, _options.Puerto, _options.Exchange);
        return _channel;
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
