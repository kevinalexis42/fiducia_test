using CoreFid.Auditoria.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CoreFid.Auditoria.Infrastructure.Messaging;

public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task Publicar(string tipo, string payload, Guid eventId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Evento publicado (log) {Tipo} {EventId} {Payload}", tipo, eventId, payload);
        return Task.CompletedTask;
    }
}
