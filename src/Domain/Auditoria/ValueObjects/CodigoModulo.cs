namespace CoreFid.Auditoria.Domain.Auditoria.ValueObjects;

public sealed record CodigoModulo
{
    public const string FondosDeInversion = "06";

    public string Valor { get; }

    private CodigoModulo(string valor) => Valor = valor;

    public static CodigoModulo? Desde(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return null;
        if (valor.Length != 2 || !char.IsDigit(valor[0]) || !char.IsDigit(valor[1]))
            throw new ArgumentException("El código de módulo debe ser numérico de dos dígitos (00-99).", nameof(valor));
        return new CodigoModulo(valor);
    }

    public bool EsFondosDeInversion => Valor == FondosDeInversion;

    public override string ToString() => Valor;
}
