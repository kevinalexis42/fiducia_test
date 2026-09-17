using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Domain.Auditoria.Events;

public sealed record AuditoriaRegistrada(
    Guid EventId,
    DateTimeOffset OccurredAt,
    long AuditoriaId,
    string CodigoEmpresa,
    string? CodigoModulo,
    string Usuario,
    string Entidad,
    string ClaveEntidad,
    string Accion,
    string Canal,
    bool EsAccionSensible) : IDomainEvent;
