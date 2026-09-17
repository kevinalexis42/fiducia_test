using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CoreFid.Auditoria.Tests.Integration.Soporte;

public sealed class AuditoriaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset AhoraFijo = new(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine")
        .WithDatabase("auditdb")
        .WithUsername("audit")
        .WithPassword("audit")
        .Build();

    public FakeTimeProvider Reloj { get; } = new(AhoraFijo);
    public PublicadorEnMemoria Publicador { get; } = new();
    public InterceptorFalloOutbox InterceptorOutbox { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuditoriaDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Auditoria", ConnectionString);
        builder.UseSetting("Database:MigrarAlIniciar", "false");
        builder.UseSetting("Outbox:Habilitado", "false");
        builder.UseSetting("RabbitMq:Habilitado", "false");
        builder.UseSetting("Jwt:Issuer", TokenDePrueba.Issuer);
        builder.UseSetting("Jwt:Audience", TokenDePrueba.Audience);
        builder.UseSetting("Jwt:SymmetricKey", TokenDePrueba.Secret);
        builder.UseSetting("Jwt:RequireHttpsMetadata", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AuditoriaDbContext>));
            services.RemoveAll(typeof(AuditoriaDbContext));
            services.AddDbContext<AuditoriaDbContext>(options =>
                options.UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)).AddInterceptors(InterceptorOutbox));

            services.RemoveAll(typeof(TimeProvider));
            services.AddSingleton<TimeProvider>(Reloj);

            services.RemoveAll(typeof(IEventPublisher));
            services.AddSingleton<IEventPublisher>(Publicador);
        });
    }

    public AuditoriaDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(ConnectionString).Options;
        return new AuditoriaDbContext(options);
    }

    public async Task LimpiarTablas()
    {
        await using var ctx = CrearContexto();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE auditoria, outbox_messages RESTART IDENTITY");
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll(this IServiceCollection services, Type serviceType)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == serviceType).ToList())
            services.Remove(descriptor);
    }
}

public sealed class PublicadorEnMemoria : IEventPublisher
{
    public List<(string Tipo, string Payload, Guid EventId)> Publicados { get; } = new();
    public bool Fallar { get; set; }

    public Task Publicar(string tipo, string payload, Guid eventId, CancellationToken cancellationToken)
    {
        if (Fallar) throw new InvalidOperationException("Broker no disponible (simulado)");
        Publicados.Add((tipo, payload, eventId));
        return Task.CompletedTask;
    }
}

public sealed class InterceptorFalloOutbox : DbCommandInterceptor
{
    public bool FallarInsertOutbox { get; set; }

    public override InterceptionResult<int> NonQueryExecuting(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Verificar(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Verificar(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result)
    {
        Verificar(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Verificar(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Verificar(System.Data.Common.DbCommand command)
    {
        if (FallarInsertOutbox && command.CommandText.Contains("INSERT INTO outbox_messages", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallo simulado al insertar en outbox");
    }
}
