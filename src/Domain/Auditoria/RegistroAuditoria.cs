using CoreFid.Auditoria.Domain.Auditoria.Events;
using CoreFid.Auditoria.Domain.Auditoria.ValueObjects;
using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Domain.Auditoria;

public sealed class RegistroAuditoria : AggregateRoot
{
    public long Id { get; private set; }
    public CodigoEmpresa CodigoEmpresa { get; private set; }
    public CodigoModulo? CodigoModulo { get; private set; }
    public string? CodigoTransaccion { get; private set; }
    public string Usuario { get; private set; }
    public string Entidad { get; private set; }
    public string ClaveEntidad { get; private set; }
    public Accion Accion { get; private set; }
    public string? ValorAnterior { get; private set; }
    public string? ValorNuevo { get; private set; }
    public string? DireccionIp { get; private set; }
    public Canal Canal { get; private set; }
    public DateTimeOffset FechaRegistro { get; private set; }

    private RegistroAuditoria()
    {
        Usuario = string.Empty;
        Entidad = string.Empty;
        ClaveEntidad = string.Empty;
    }

    private RegistroAuditoria(
        CodigoEmpresa codigoEmpresa,
        CodigoModulo? codigoModulo,
        string? codigoTransaccion,
        string usuario,
        string entidad,
        string claveEntidad,
        Accion accion,
        string? valorAnterior,
        string? valorNuevo,
        string? direccionIp,
        Canal canal,
        DateTimeOffset fechaRegistro)
    {
        CodigoEmpresa = codigoEmpresa;
        CodigoModulo = codigoModulo;
        CodigoTransaccion = codigoTransaccion;
        Usuario = usuario;
        Entidad = entidad;
        ClaveEntidad = claveEntidad;
        Accion = accion;
        ValorAnterior = valorAnterior;
        ValorNuevo = valorNuevo;
        DireccionIp = direccionIp;
        Canal = canal;
        FechaRegistro = fechaRegistro;
    }

    public static RegistroAuditoria Registrar(
        string? codigoEmpresa,
        string? codigoModulo,
        string? codigoTransaccion,
        string? usuario,
        string? entidad,
        string? claveEntidad,
        string? accion,
        string? valorAnterior,
        string? valorNuevo,
        string? direccionIp,
        string? canal,
        DateTimeOffset ahora)
    {
        var canalVo = Canal.Desde(canal);

        if (canalVo.EsBatch && accion == "Q")
            throw new DomainException(CodigosError.BatchNoRegistraConsultas, "El canal BATCH no registra consultas.");

        var empresaVo = CodigoEmpresa.Desde(codigoEmpresa);

        if (string.IsNullOrEmpty(usuario))
            throw new DomainException(CodigosError.UsuarioRequerido, "Usuario requerido.");

        var accionVo = Accion.Desde(accion);

        if (accionVo.EsActualizacion && string.IsNullOrEmpty(valorAnterior))
            throw new DomainException(CodigosError.ValorAnteriorRequerido, "Una actualización requiere valor anterior.");

        if (string.IsNullOrEmpty(claveEntidad))
            throw new DomainException(CodigosError.ClaveEntidadRequerida, "Clave de entidad requerida.");

        return new RegistroAuditoria(
            empresaVo,
            CodigoModulo.Desde(codigoModulo),
            codigoTransaccion,
            usuario,
            entidad ?? string.Empty,
            claveEntidad,
            accionVo,
            valorAnterior,
            valorNuevo,
            direccionIp,
            canalVo,
            ahora);
    }

    public bool EsAccionSensible =>
        Accion.EsEliminacion
        || CodigoModulo?.EsFondosDeInversion == true
        || Entidad == "Usuario"
        || Entidad == "Rol";

    public void ConfirmarRegistro()
    {
        if (Id == 0)
            throw new InvalidOperationException("El registro de auditoría aún no tiene identidad asignada.");
        if (DomainEvents.Count > 0)
            throw new InvalidOperationException("El registro de auditoría ya fue confirmado.");

        RaiseDomainEvent(new AuditoriaRegistrada(
            Guid.NewGuid(),
            FechaRegistro,
            Id,
            CodigoEmpresa.Valor,
            CodigoModulo?.Valor,
            Usuario,
            Entidad,
            ClaveEntidad,
            Accion.Valor,
            Canal.Valor,
            EsAccionSensible));
    }
}
