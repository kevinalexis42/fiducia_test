using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Api.Contratos;

public sealed record RespuestaPlataforma<T>(string Codigo, string Mensaje, string? Detalle, T? Data)
{
    public static RespuestaPlataforma<T> Ok(string mensaje, T data) => new(CodigosError.Ok, mensaje, null, data);

    public static RespuestaPlataforma<T> Error(string codigo, string mensaje, string? detalle = null) =>
        new(codigo, mensaje, detalle, default);
}
