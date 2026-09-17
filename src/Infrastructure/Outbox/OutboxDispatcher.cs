using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreFid.Auditoria.Infrastructure.Outbox;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _reloj;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        TimeProvider reloj,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _reloj = reloj;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Habilitado)
        {
            _logger.LogInformation("Outbox dispatcher deshabilitado por configuración");
            return;
        }

        _logger.LogInformation("Outbox dispatcher iniciado, intervalo {Intervalo}s, lote {Lote}",
            _options.IntervaloSegundos, _options.TamanoLote);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervaloSegundos), _reloj);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var procesados = await ProcesarLote(stoppingToken);
                if (procesados == _options.TamanoLote) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo inesperado en el ciclo del outbox dispatcher");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> ProcesarLote(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditoriaDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var estrategia = context.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () => await ProcesarLoteTransaccional(context, publisher, cancellationToken));
    }

    private async Task<int> ProcesarLoteTransaccional(AuditoriaDbContext context, IEventPublisher publisher, CancellationToken cancellationToken)
    {
        await using var transaccion = await context.Database.BeginTransactionAsync(cancellationToken);

        var ahora = _reloj.GetUtcNow();
        var pendientes = await context.OutboxMessages
            .FromSqlInterpolated($@"
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {ahora})
                ORDER BY occurred_at
                LIMIT {_options.TamanoLote}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
        {
            await transaccion.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (var mensaje in pendientes)
        {
            try
            {
                await publisher.Publicar(mensaje.Tipo, mensaje.Payload, mensaje.Id, cancellationToken);
                mensaje.MarcarProcesado(_reloj.GetUtcNow());
                _logger.LogInformation("Evento {Tipo} {EventId} publicado", mensaje.Tipo, mensaje.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var backoff = CalcularBackoff(mensaje.Attempts + 1);
                mensaje.MarcarFallo(ex.Message, _reloj.GetUtcNow(), backoff);
                _logger.LogWarning(ex, "Fallo publicando {Tipo} {EventId}, intento {Intento}, reintento en {Backoff}s",
                    mensaje.Tipo, mensaje.Id, mensaje.Attempts, backoff.TotalSeconds);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
        return pendientes.Count;
    }

    private TimeSpan CalcularBackoff(int intento)
    {
        var segundos = _options.BackoffBaseSegundos * Math.Pow(2, Math.Min(intento - 1, 10));
        return TimeSpan.FromSeconds(Math.Min(segundos, _options.BackoffMaximoSegundos));
    }
}
public sealed class OutboxOptions
{
    public const string Seccion = "Outbox";

    public bool Habilitado { get; set; } = true;
    public int IntervaloSegundos { get; set; } = 5;
    public int TamanoLote { get; set; } = 50;
    public int BackoffBaseSegundos { get; set; } = 5;
    public int BackoffMaximoSegundos { get; set; } = 300;
}
