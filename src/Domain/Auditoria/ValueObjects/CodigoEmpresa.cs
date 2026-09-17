using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Domain.Auditoria.ValueObjects;

public readonly record struct CodigoEmpresa
{
    public const int LongitudMaxima = 4;

    public string Valor { get; }

    private CodigoEmpresa(string valor) => Valor = valor;

    public static CodigoEmpresa Desde(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
            throw new DomainException(CodigosError.EmpresaRequerida, "Código de empresa requerido.");
        if (valor.Length > LongitudMaxima)
            throw new ArgumentException($"El código de empresa no puede exceder {LongitudMaxima} caracteres.", nameof(valor));
        return new CodigoEmpresa(valor);
    }

    public override string ToString() => Valor;
}
