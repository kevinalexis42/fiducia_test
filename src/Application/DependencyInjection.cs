using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Commands;
using CoreFid.Auditoria.Application.Auditoria.Dtos;
using CoreFid.Auditoria.Application.Auditoria.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace CoreFid.Auditoria.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<RegistrarAuditoriaCommand, long>, RegistrarAuditoriaHandler>();
        services.AddScoped<IQueryHandler<ConsultarAuditoriaQuery, IReadOnlyList<RegistroAuditoriaDto>>, ConsultarAuditoriaHandler>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
