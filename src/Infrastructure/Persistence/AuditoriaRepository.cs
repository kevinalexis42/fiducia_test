using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Domain.Auditoria;

namespace CoreFid.Auditoria.Infrastructure.Persistence;

public sealed class AuditoriaRepository : IAuditoriaRepository
{
    private readonly AuditoriaDbContext _context;

    public AuditoriaRepository(AuditoriaDbContext context)
    {
        _context = context;
    }

    public void Agregar(RegistroAuditoria registro) => _context.Auditorias.Add(registro);
}
