using System.Net.Http.Headers;
using System.Net.Http.Json;
using CoreFid.Auditoria.Infrastructure.Outbox;
using CoreFid.Auditoria.Tests.Integration.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreFid.Auditoria.Tests.Integration;

[Collection(nameof(ApiCollection))]
public class OutboxDispatcherTests : IAsyncLifetime
{
    private readonly AuditoriaApiFactory _factory;
    private readonly HttpClient _client;

    public OutboxDispatcherTests(AuditoriaApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenDePrueba.Generar());
    }

    public Task InitializeAsync()
    {
        _factory.Publicador.Publicados.Clear();
        _factory.Publicador.Fallar = false;
        return _factory.LimpiarTablas();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OutboxDispatcher CrearDispatcher() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Reloj,
        Options.Create(new OutboxOptions { TamanoLote = 10, BackoffBaseSegundos = 5, BackoffMaximoSegundos = 300 }),
        NullLogger<OutboxDispatcher>.Instance);

    private async Task RegistrarN(int n)
    {
        for (var i = 0; i < n; i++)
            await _client.PostAsJsonAsync("/api/auditoria/registro", new
            {
                codigoEmpresa = "0001", usuario = "jperez", entidad = "Cliente",
                claveEntidad = $"CLI-{i}", accion = "C", canal = "API"
            });
    }

    [Fact]
    public async Task Publica_pendientes_marca_processed_at_y_no_los_vuelve_a_publicar()
    {
        await RegistrarN(3);
        var ahora = _factory.Reloj.GetUtcNow();
        var dispatcher = CrearDispatcher();

        var procesados = await dispatcher.ProcesarLote(CancellationToken.None);
        var segundaVuelta = await dispatcher.ProcesarLote(CancellationToken.None);

        Assert.Equal(3, procesados);
        Assert.Equal(0, segundaVuelta);
        Assert.Equal(3, _factory.Publicador.Publicados.Count);
        Assert.Equal(3, _factory.Publicador.Publicados.Select(p => p.EventId).Distinct().Count());

        await using var ctx = _factory.CrearContexto();
        Assert.Equal(3, await ctx.OutboxMessages.CountAsync(m => m.ProcessedAt == ahora));
    }

    [Fact]
    public async Task Con_el_broker_caido_registra_el_fallo_aplica_backoff_y_reintenta_despues()
    {
        await RegistrarN(1);
        var ahora = _factory.Reloj.GetUtcNow();
        var dispatcher = CrearDispatcher();
        _factory.Publicador.Fallar = true;

        await dispatcher.ProcesarLote(CancellationToken.None);

        await using (var ctx = _factory.CrearContexto())
        {
            var mensaje = await ctx.OutboxMessages.SingleAsync();
            Assert.Null(mensaje.ProcessedAt);
            Assert.Equal(1, mensaje.Attempts);
            Assert.Contains("Broker no disponible", mensaje.Error);
            Assert.Equal(ahora.AddSeconds(5), mensaje.NextAttemptAt);
        }

        Assert.Equal(0, await dispatcher.ProcesarLote(CancellationToken.None));

        _factory.Publicador.Fallar = false;
        _factory.Reloj.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(1, await dispatcher.ProcesarLote(CancellationToken.None));
        Assert.Single(_factory.Publicador.Publicados);

        await using (var ctx = _factory.CrearContexto())
        {
            var mensaje = await ctx.OutboxMessages.SingleAsync();
            Assert.NotNull(mensaje.ProcessedAt);
            Assert.Null(mensaje.Error);
        }
    }

    [Fact]
    public async Task El_backoff_crece_exponencialmente_hasta_el_maximo()
    {
        await RegistrarN(1);
        var ahora = _factory.Reloj.GetUtcNow();
        var dispatcher = CrearDispatcher();
        _factory.Publicador.Fallar = true;

        var esperados = new[] { 5, 10, 20, 40, 80, 160, 300, 300 };
        foreach (var esperado in esperados)
        {
            await dispatcher.ProcesarLote(CancellationToken.None);
            await using var ctx = _factory.CrearContexto();
            var mensaje = await ctx.OutboxMessages.SingleAsync();
            Assert.Equal(_factory.Reloj.GetUtcNow().AddSeconds(esperado), mensaje.NextAttemptAt);
            _factory.Reloj.Advance(TimeSpan.FromSeconds(esperado));
        }
    }
}
