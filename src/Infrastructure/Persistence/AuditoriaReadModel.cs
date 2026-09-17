using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Dtos;
using CoreFid.Auditoria.Domain.Auditoria.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CoreFid.Auditoria.Infrastructure.Persistence;

public sealed class AuditoriaReadModel : IAuditoriaReadModel
{
    private readonly AuditoriaDbContext _context;

    public AuditoriaReadModel(AuditoriaDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RegistroAuditoriaDto>> Buscar(CriterioAuditoria criterio, CancellationToken cancellationToken)
    {
        var empresa = CodigoEmpresa.Desde(criterio.CodigoEmpresa);

        var consulta = _context.Auditorias
            .AsNoTracking()
            .Where(a => a.CodigoEmpresa == empresa);

        if (criterio.Usuario is not null)
            consulta = consulta.Where(a => a.Usuario == criterio.Usuario);

        if (criterio.Entidad is not null)
            consulta = consulta.Where(a => EF.Functions.ILike(a.Entidad, $"%{criterio.Entidad}%"));

        if (criterio.FechaDesde is { } fechaDesde)
        {
            var desde = new DateTimeOffset(fechaDesde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(a => a.FechaRegistro >= desde);
        }

        consulta = consulta.OrderByDescending(a => a.FechaRegistro);

        if (criterio.MaximoResultados is { } maximo)
            consulta = consulta.Take(maximo);

        var filas = await consulta.ToListAsync(cancellationToken);

        return filas
            .Select(a => new RegistroAuditoriaDto(
                a.Id,
                a.CodigoEmpresa.Valor,
                a.CodigoModulo?.Valor,
                a.CodigoTransaccion,
                a.Usuario,
                a.Entidad,
                a.ClaveEntidad,
                a.Accion.Valor,
                a.ValorAnterior,
                a.ValorNuevo,
                a.DireccionIp,
                a.Canal.Valor,
                a.FechaRegistro))
            .ToList();
    }
}
