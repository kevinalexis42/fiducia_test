namespace CoreFid.Auditoria.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Tipo { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    private OutboxMessage()
    {
        Tipo = string.Empty;
        Payload = string.Empty;
    }

    public OutboxMessage(Guid id, string tipo, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        Tipo = tipo;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    public void MarcarProcesado(DateTimeOffset ahora)
    {
        ProcessedAt = ahora;
        Error = null;
    }

    public void MarcarFallo(string error, DateTimeOffset ahora, TimeSpan backoff)
    {
        Attempts++;
        Error = error.Length > 2000 ? error[..2000] : error;
        NextAttemptAt = ahora + backoff;
    }
}
