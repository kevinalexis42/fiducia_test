namespace CoreFid.Auditoria.Domain.Auditoria.ValueObjects;

public readonly record struct Canal
{
    public static readonly Canal Web = new("WEB");
    public static readonly Canal Api = new("API");
    public static readonly Canal Batch = new("BATCH");

    private static readonly string[] Validos = { "WEB", "API", "BATCH" };

    public string Valor { get; }

    private Canal(string valor) => Valor = valor;

    public static Canal Desde(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return Web;
        if (!EsValido(valor))
            throw new ArgumentException($"Canal '{valor}' no reconocido. Valores válidos: WEB, API, BATCH.", nameof(valor));
        return new Canal(valor);
    }

    public static bool EsValido(string? valor) => string.IsNullOrEmpty(valor) || Array.IndexOf(Validos, valor) >= 0;

    public bool EsBatch => Valor == "BATCH";

    public override string ToString() => Valor;
}
