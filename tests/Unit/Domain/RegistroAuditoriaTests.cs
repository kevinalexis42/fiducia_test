using CoreFid.Auditoria.Domain.Auditoria;
using CoreFid.Auditoria.Domain.Auditoria.Events;
using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Tests.Unit.Domain;

public class RegistroAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 16, 15, 0, 0, TimeSpan.Zero);

    private static RegistroAuditoria Crear(
        string? codigoEmpresa = "0001",
        string? codigoModulo = "02",
        string? usuario = "jperez",
        string? entidad = "SolicitudRescate",
        string? claveEntidad = "RES-2026-004871",
        string? accion = "C",
        string? valorAnterior = null,
        string? valorNuevo = "{\"estado\":\"APROBADA\"}",
        string? canal = "API") =>
        RegistroAuditoria.Registrar(
            codigoEmpresa, codigoModulo, "V062014", usuario, entidad, claveEntidad,
            accion, valorAnterior, valorNuevo, "10.0.4.77", canal, Ahora);

    [Fact]
    public void Registro_valido_se_construye_con_la_fecha_inyectada()
    {
        var registro = Crear();

        Assert.Equal("0001", registro.CodigoEmpresa.Valor);
        Assert.Equal("C", registro.Accion.Valor);
        Assert.Equal("API", registro.Canal.Valor);
        Assert.Equal(Ahora, registro.FechaRegistro);
        Assert.Equal(0, registro.Id);
        Assert.Empty(registro.DomainEvents);
    }

    [Fact]
    public void Sin_codigo_de_empresa_lanza_E01()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(codigoEmpresa: ""));
        Assert.Equal(CodigosError.EmpresaRequerida, ex.Codigo);
    }

    [Fact]
    public void Sin_usuario_lanza_E02()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(usuario: null));
        Assert.Equal(CodigosError.UsuarioRequerido, ex.Codigo);
    }

    [Theory]
    [InlineData("X")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("c")]
    public void Accion_fuera_de_C_U_D_Q_lanza_E03(string? accion)
    {
        var ex = Assert.Throws<DomainException>(() => Crear(accion: accion));
        Assert.Equal(CodigosError.AccionInvalida, ex.Codigo);
    }

    [Fact]
    public void Actualizacion_sin_valor_anterior_lanza_E04()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(accion: "U", valorAnterior: ""));
        Assert.Equal(CodigosError.ValorAnteriorRequerido, ex.Codigo);
    }

    [Fact]
    public void Actualizacion_con_valor_anterior_es_valida()
    {
        var registro = Crear(accion: "U", valorAnterior: "{\"estado\":\"PENDIENTE\"}");
        Assert.True(registro.Accion.EsActualizacion);
    }

    [Fact]
    public void Sin_clave_de_entidad_lanza_E05()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(claveEntidad: null));
        Assert.Equal(CodigosError.ClaveEntidadRequerida, ex.Codigo);
    }

    [Fact]
    public void Canal_BATCH_con_accion_Q_lanza_E10()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(canal: "BATCH", accion: "Q"));
        Assert.Equal(CodigosError.BatchNoRegistraConsultas, ex.Codigo);
    }

    [Fact]
    public void Canal_BATCH_con_otra_accion_es_valido()
    {
        var registro = Crear(canal: "BATCH", accion: "C");
        Assert.True(registro.Canal.EsBatch);
    }

    [Fact]
    public void La_regla_E10_se_evalua_antes_que_las_demas_como_en_el_legacy()
    {
        var ex = Assert.Throws<DomainException>(() => Crear(canal: "BATCH", accion: "Q", codigoEmpresa: ""));
        Assert.Equal(CodigosError.BatchNoRegistraConsultas, ex.Codigo);
    }

    [Fact]
    public void Los_errores_se_evaluan_en_el_orden_E01_E02_E03_E04_E05()
    {
        Assert.Equal(CodigosError.EmpresaRequerida,
            Assert.Throws<DomainException>(() => Crear(codigoEmpresa: "", usuario: "", accion: "X", claveEntidad: "")).Codigo);
        Assert.Equal(CodigosError.UsuarioRequerido,
            Assert.Throws<DomainException>(() => Crear(usuario: "", accion: "X", claveEntidad: "")).Codigo);
        Assert.Equal(CodigosError.AccionInvalida,
            Assert.Throws<DomainException>(() => Crear(accion: "X", claveEntidad: "")).Codigo);
        Assert.Equal(CodigosError.ValorAnteriorRequerido,
            Assert.Throws<DomainException>(() => Crear(accion: "U", claveEntidad: "")).Codigo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Canal_vacio_se_normaliza_a_WEB(string? canal)
    {
        var registro = Crear(canal: canal);
        Assert.Equal("WEB", registro.Canal.Valor);
    }

    [Fact]
    public void Canal_desconocido_es_error_de_formato_no_de_negocio()
    {
        Assert.Throws<ArgumentException>(() => Crear(canal: "MOBILE"));
    }

    [Theory]
    [InlineData("6")]
    [InlineData("ABC")]
    [InlineData("0A")]
    public void Codigo_de_modulo_no_numerico_de_dos_digitos_es_error_de_formato(string codigoModulo)
    {
        Assert.Throws<ArgumentException>(() => Crear(codigoModulo: codigoModulo));
    }

    [Theory]
    [InlineData("D", "02", "Cliente", true)]
    [InlineData("C", "06", "Cliente", true)]
    [InlineData("U", "02", "Usuario", true)]
    [InlineData("C", "02", "Rol", true)]
    [InlineData("C", "02", "Cliente", false)]
    [InlineData("Q", "03", "Reporte", false)]
    public void Accion_sensible_replica_la_regla_del_legacy(string accion, string modulo, string entidad, bool esperado)
    {
        var registro = Crear(accion: accion, codigoModulo: modulo, entidad: entidad,
            valorAnterior: accion == "U" ? "{}" : null);
        Assert.Equal(esperado, registro.EsAccionSensible);
    }

    [Fact]
    public void Confirmar_sin_identidad_falla()
    {
        var registro = Crear();
        Assert.Throws<InvalidOperationException>(() => registro.ConfirmarRegistro());
    }

    [Fact]
    public void Confirmar_con_identidad_emite_AuditoriaRegistrada_una_sola_vez()
    {
        var registro = Crear(accion: "D", entidad: "Usuario");
        typeof(RegistroAuditoria).GetProperty(nameof(RegistroAuditoria.Id))!.SetValue(registro, 884213L);

        registro.ConfirmarRegistro();

        var evento = Assert.Single(registro.DomainEvents);
        var registrada = Assert.IsType<AuditoriaRegistrada>(evento);
        Assert.Equal(884213L, registrada.AuditoriaId);
        Assert.Equal("D", registrada.Accion);
        Assert.True(registrada.EsAccionSensible);
        Assert.Equal(Ahora, registrada.OccurredAt);
        Assert.Throws<InvalidOperationException>(() => registro.ConfirmarRegistro());
    }

    [Fact]
    public void El_agregado_es_inmutable_no_expone_setters_publicos()
    {
        var setters = typeof(RegistroAuditoria).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(setters);
    }
}
