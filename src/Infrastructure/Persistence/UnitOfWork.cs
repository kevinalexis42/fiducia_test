using System.Text.Json;
using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Domain.Auditoria;
using CoreFid.Auditoria.Domain.Common;
using CoreFid.Auditoria.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CoreFid.Auditoria.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly AuditoriaDbContext _context;

    public UnitOfWork(AuditoriaDbContext context)
    {
        _context = context;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var estrategia = _context.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            var nuevos = _context.ChangeTracker.Entries<RegistroAuditoria>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();

            await using var transaccion = await _context.Database.BeginTransactionAsync(cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var agregado in nuevos)
                agregado.ConfirmarRegistro();

            var mensajes = nuevos
                .SelectMany(a => a.DomainEvents)
                .Select(ToOutboxMessage)
                .ToList();

            if (mensajes.Count > 0)
            {
                _context.OutboxMessages.AddRange(mensajes);
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaccion.CommitAsync(cancellationToken);

            foreach (var agregado in nuevos)
                agregado.ClearDomainEvents();
        });
    }

    private static OutboxMessage ToOutboxMessage(IDomainEvent evento) =>
        new(
            evento.EventId,
            evento.GetType().Name,
            JsonSerializer.Serialize(evento, evento.GetType(), PayloadJson),
            evento.OccurredAt);
}
