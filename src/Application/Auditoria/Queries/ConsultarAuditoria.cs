using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Dtos;
using CoreFid.Auditoria.Domain.Common;

namespace CoreFid.Auditoria.Application.Auditoria.Queries;

public sealed record ConsultarAuditoriaQuery(
    string? CodigoEmpresa,
    string? Usuario,
    string? Entidad,
    DateOnly? FechaDesde) : IQuery<IReadOnlyList<RegistroAuditoriaDto>>;

public sealed class ConsultarAuditoriaHandler : IQueryHandler<ConsultarAuditoriaQuery, IReadOnlyList<RegistroAuditoriaDto>>
{
    private readonly IAuditoriaReadModel _readModel;
    private readonly ConsultaOptions _options;

    public ConsultarAuditoriaHandler(IAuditoriaReadModel readModel, ConsultaOptions options)
    {
        _readModel = readModel;
        _options = options;
    }

    public Task<IReadOnlyList<RegistroAuditoriaDto>> Handle(ConsultarAuditoriaQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(query.CodigoEmpresa))
            throw new DomainException(CodigosError.EmpresaRequerida, "Código de empresa requerido.");

        var criterio = new CriterioAuditoria(
            query.CodigoEmpresa,
            string.IsNullOrEmpty(query.Usuario) ? null : query.Usuario,
            string.IsNullOrEmpty(query.Entidad) ? null : query.Entidad,
            query.FechaDesde,
            _options.MaximoResultados > 0 ? _options.MaximoResultados : null);

        return _readModel.Buscar(criterio, cancellationToken);
    }
}

public sealed class ConsultaOptions
{
    public const string Seccion = "Consulta";

    public int MaximoResultados { get; set; }
}
