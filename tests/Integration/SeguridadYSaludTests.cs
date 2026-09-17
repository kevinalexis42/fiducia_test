using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CoreFid.Auditoria.Tests.Integration.Soporte;

namespace CoreFid.Auditoria.Tests.Integration;

[Collection(nameof(ApiCollection))]
public class SeguridadYSaludTests
{
    private readonly AuditoriaApiFactory _factory;

    public SeguridadYSaludTests(AuditoriaApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Cliente(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var respuesta = await Cliente(null).GetAsync("/api/auditoria/consulta?codigoEmpresa=0001");
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("firma")]
    [InlineData("expirado")]
    public async Task Token_invalido_responde_401(string caso)
    {
        var token = caso switch
        {
            "issuer" => TokenDePrueba.Generar(issuer: "https://otro-tenant/"),
            "audience" => TokenDePrueba.Generar(audience: "api://otra-api"),
            "firma" => TokenDePrueba.Generar(secret: "otra-clave-que-no-es-la-configurada-0123456789"),
            _ => TokenDePrueba.Generar(expira: DateTime.UtcNow.AddMinutes(-5))
        };

        var respuesta = await Cliente(token).GetAsync("/api/auditoria/consulta?codigoEmpresa=0001");
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_sin_rol_de_escritura_no_puede_registrar()
    {
        var respuesta = await Cliente(TokenDePrueba.Generar(roles: new[] { "audit.read" }))
            .PostAsJsonAsync("/api/auditoria/registro", new { codigoEmpresa = "0001" });
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Healthz_y_readyz_responden_sin_autenticacion()
    {
        var cliente = Cliente(null);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/readyz")).StatusCode);
    }

    [Fact]
    public async Task Swagger_expone_los_dos_endpoints_del_contrato()
    {
        var swagger = await Cliente(null).GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/auditoria/registro", swagger);
        Assert.Contains("/api/auditoria/consulta", swagger);
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<AuditoriaApiFactory>;
