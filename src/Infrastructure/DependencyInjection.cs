using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Queries;
using CoreFid.Auditoria.Infrastructure.Messaging;
using CoreFid.Auditoria.Infrastructure.Outbox;
using CoreFid.Auditoria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoreFid.Auditoria.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Auditoria")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Auditoria'.");

        services.AddDbContext<AuditoriaDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        services.AddScoped<IAuditoriaRepository, AuditoriaRepository>();
        services.AddScoped<IAuditoriaReadModel, AuditoriaReadModel>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.Configure<ConsultaOptions>(configuration.GetSection(ConsultaOptions.Seccion));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ConsultaOptions>>().Value);

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Seccion));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Seccion));

        var rabbitHabilitado = configuration.GetValue<bool>($"{RabbitMqOptions.Seccion}:Habilitado");
        if (rabbitHabilitado)
            services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        else
            services.AddSingleton<IEventPublisher, LoggingEventPublisher>();

        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
