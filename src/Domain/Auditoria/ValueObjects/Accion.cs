using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Domain.Auditoria.ValueObjects;

public readonly record struct Accion
{
    public static readonly Accion Crear = new("C");
    public static readonly Accion Actualizar = new("U");
    public static readonly Accion Eliminar = new("D");
    public static readonly Accion Consultar = new("Q");

    private static readonly string[] Validas = { "C", "U", "D", "Q" };

    public string Valor { get; }

    private Accion(string valor) => Valor = valor;

    public static Accion Desde(string? valor)
    {
        if (!EsValida(valor))
            throw new DomainException(CodigosError.AccionInvalida, "Acción inválida.");
        return new Accion(valor!);
    }

    public static bool EsValida(string? valor) => valor is not null && Array.IndexOf(Validas, valor) >= 0;

    public bool EsActualizacion => Valor == "U";
    public bool EsEliminacion => Valor == "D";
    public bool EsConsulta => Valor == "Q";

    public override string ToString() => Valor;
}
