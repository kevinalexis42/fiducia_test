namespace CoreFid.Auditoria.Domain.Common;

public sealed class DomainException : Exception
{
    public string Codigo { get; }

    public DomainException(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }
}
