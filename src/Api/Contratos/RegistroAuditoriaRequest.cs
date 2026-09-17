using System.ComponentModel.DataAnnotations;

namespace CoreFid.Auditoria.Api.Contratos;

public sealed class RegistroAuditoriaRequest
{
    [MaxLength(4)] public string? CodigoEmpresa { get; init; }
    [MaxLength(2)] public string? CodigoModulo { get; init; }
    [MaxLength(10)] public string? CodigoTransaccion { get; init; }
    [MaxLength(30)] public string? Usuario { get; init; }
    [MaxLength(60)] public string? Entidad { get; init; }
    [MaxLength(60)] public string? ClaveEntidad { get; init; }
    [MaxLength(1)] public string? Accion { get; init; }
    public string? ValorAnterior { get; init; }
    public string? ValorNuevo { get; init; }
    [MaxLength(45)] public string? DireccionIp { get; init; }
    [RegularExpression("^(WEB|API|BATCH)?$")] public string? Canal { get; init; }
}