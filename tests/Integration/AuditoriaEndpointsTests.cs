using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoreFid.Auditoria.Tests.Integration.Soporte;
using Microsoft.EntityFrameworkCore;

namespace CoreFid.Auditoria.Tests.Integration;

[Collection(nameof(ApiCollection))]
public class AuditoriaEndpointsTests : IAsyncLifetime
{
    private readonly AuditoriaApiFactory _factory;
    private readonly HttpClient _client;

    public AuditoriaEndpointsTests(AuditoriaApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenDePrueba.Generar());
    }

    public Task InitializeAsync()
    {
        _factory.InterceptorOutbox.FallarInsertOutbox = false;
        return _factory.LimpiarTablas();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object PeticionValida(string accion = "C", string? valorAnterior = null, string? canal = "API") => new
    {
        codigoEmpresa = "0001",
        codigoModulo = "06",
        codigoTransaccion = "V062014",
        usuario = "jperez",
        entidad = "SolicitudRescate",
        claveEntidad = "RES-2026-004871",
        accion,
        valorAnterior,
        valorNuevo = "{\"estado\":\"APROBADA\"}",
        direccionIp = "10.0.4.77",
        canal
    };

    [Fact]
    public async Task Registro_valido_responde_200_con_el_envoltorio_del_contrato_y_el_id()
    {
        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await LeerJson(respuesta);
        Assert.Equal(new[] { "codigo", "mensaje", "detalle", "data" }, cuerpo.Select(kv => kv.Key).ToArray());
        Assert.Equal("OK", cuerpo["codigo"]!.GetValue<string>());
        Assert.Equal("Registro creado.", cuerpo["mensaje"]!.GetValue<string>());
        Assert.Null(cuerpo["detalle"]);
        Assert.True(cuerpo["data"]!.GetValue<long>() > 0);
    }

    [Fact]
    public async Task Registro_escribe_auditoria_y_outbox_en_la_misma_transaccion()
    {
        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida(accion: "D"));
        var id = (await LeerJson(respuesta))["data"]!.GetValue<long>();

        await using var ctx = _factory.CrearContexto();
        var auditoria = await ctx.Auditorias.SingleAsync(a => a.Id == id);
        var outbox = await ctx.OutboxMessages.SingleAsync();

        Assert.Equal("AuditoriaRegistrada", outbox.Tipo);
        Assert.Null(outbox.ProcessedAt);
        Assert.Equal(_factory.Reloj.GetUtcNow(), auditoria.FechaRegistro);
        Assert.Equal(_factory.Reloj.GetUtcNow(), outbox.OccurredAt);

        var payload = JsonNode.Parse(outbox.Payload)!;
        Assert.Equal(id, payload["auditoriaId"]!.GetValue<long>());
        Assert.Equal("D", payload["accion"]!.GetValue<string>());
        Assert.True(payload["esAccionSensible"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Si_falla_el_insert_del_outbox_tampoco_queda_la_auditoria()
    {
        _factory.InterceptorOutbox.FallarInsertOutbox = true;

        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida());

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        var cuerpo = await LeerJson(respuesta);
        Assert.Equal("E99", cuerpo["codigo"]!.GetValue<string>());
        Assert.DoesNotContain("   at ", cuerpo["detalle"]!.GetValue<string>());

        await using var ctx = _factory.CrearContexto();
        Assert.Equal(0, await ctx.Auditorias.CountAsync());
        Assert.Equal(0, await ctx.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("codigoEmpresa", "", "E01")]
    [InlineData("usuario", "", "E02")]
    [InlineData("accion", "X", "E03")]
    [InlineData("claveEntidad", "", "E05")]
    public async Task Errores_de_negocio_responden_200_con_el_codigo_legacy(string campo, string valor, string codigoEsperado)
    {
        var peticion = JsonSerializer.SerializeToNode(PeticionValida())!.AsObject();
        peticion[campo] = valor;

        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", peticion);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await LeerJson(respuesta);
        Assert.Equal(codigoEsperado, cuerpo["codigo"]!.GetValue<string>());
        Assert.Null(cuerpo["data"]);

        await using var ctx = _factory.CrearContexto();
        Assert.Equal(0, await ctx.Auditorias.CountAsync());
    }

    [Fact]
    public async Task Actualizacion_sin_valor_anterior_responde_200_E04()
    {
        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida(accion: "U", valorAnterior: null));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("E04", (await LeerJson(respuesta))["codigo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Canal_BATCH_con_consulta_responde_400_E10_como_el_legacy()
    {
        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida(accion: "Q", canal: "BATCH"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await LeerJson(respuesta);
        Assert.Equal("E10", cuerpo["codigo"]!.GetValue<string>());
        Assert.Equal("El canal BATCH no registra consultas.", cuerpo["mensaje"]!.GetValue<string>());
    }

    [Fact]
    public async Task Peticion_fuera_del_contrato_responde_400_E00()
    {
        var peticion = JsonSerializer.SerializeToNode(PeticionValida())!.AsObject();
        peticion["codigoEmpresa"] = "DEMASIADO-LARGO";
        peticion["canal"] = "MOBILE";

        var respuesta = await _client.PostAsJsonAsync("/api/auditoria/registro", peticion);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await LeerJson(respuesta);
        Assert.Equal("E00", cuerpo["codigo"]!.GetValue<string>());
        Assert.Contains("CodigoEmpresa", cuerpo["detalle"]!.GetValue<string>());
        Assert.Contains("Canal", cuerpo["detalle"]!.GetValue<string>());
    }

    [Fact]
    public async Task Consulta_devuelve_la_lista_con_el_shape_del_contrato_y_filtra_por_criterios()
    {
        await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida());
        var otra = JsonSerializer.SerializeToNode(PeticionValida())!.AsObject();
        otra["usuario"] = "mlopez";
        otra["entidad"] = "Cliente";
        await _client.PostAsJsonAsync("/api/auditoria/registro", otra);

        var todas = await LeerJson(await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0001"));
        Assert.Equal("OK", todas["codigo"]!.GetValue<string>());
        Assert.Equal("Consulta exitosa", todas["mensaje"]!.GetValue<string>());
        Assert.Equal(2, todas["data"]!.AsArray().Count);

        var primero = todas["data"]!.AsArray()[0]!.AsObject();
        Assert.Equal(
            new[] { "id", "codigoEmpresa", "codigoModulo", "codigoTransaccion", "usuario", "entidad", "claveEntidad",
                    "accion", "valorAnterior", "valorNuevo", "direccionIp", "canal", "fechaRegistro" },
            primero.Select(kv => kv.Key).ToArray());

        var porUsuario = await LeerJson(await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0001&usuario=mlopez"));
        Assert.Single(porUsuario["data"]!.AsArray());

        var porEntidad = await LeerJson(await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0001&entidad=cliente"));
        Assert.Single(porEntidad["data"]!.AsArray());

        var futura = await LeerJson(await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0001&fechaDesde=2030-01-01"));
        Assert.Empty(futura["data"]!.AsArray());

        var otraEmpresa = await LeerJson(await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0002"));
        Assert.Empty(otraEmpresa["data"]!.AsArray());
    }

    [Fact]
    public async Task Consulta_con_entidad_maliciosa_no_es_inyectable()
    {
        await _client.PostAsJsonAsync("/api/auditoria/registro", PeticionValida());

        var respuesta = await _client.GetAsync("/api/auditoria/consulta?codigoEmpresa=0001&usuario=" + Uri.EscapeDataString("x' OR '1'='1"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty((await LeerJson(respuesta))["data"]!.AsArray());
    }

    [Fact]
    public async Task Consulta_sin_codigo_de_empresa_responde_400_E01()
    {
        var respuesta = await _client.GetAsync("/api/auditoria/consulta");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("E01", (await LeerJson(respuesta))["codigo"]!.GetValue<string>());
    }

    private static async Task<JsonObject> LeerJson(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        return JsonNode.Parse(texto)!.AsObject();
    }
}
