namespace CoreFid.Auditoria.Application.Auditoria.Dtos;

public sealed record RegistroAuditoriaDto(
    long Id,
    string CodigoEmpresa,
    string? CodigoModulo,
    string? CodigoTransaccion,
    string Usuario,
    string Entidad,
    string ClaveEntidad,
    string Accion,
    string? ValorAnterior,
    string? ValorNuevo,
    string? DireccionIp,
    string Canal,
    DateTimeOffset FechaRegistro);
