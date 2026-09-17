using CoreFid.Auditoria.Application.Auditoria.Dtos;
using CoreFid.Auditoria.Domain.Auditoria;

namespace CoreFid.Auditoria.Application.Abstractions;

public interface ICommand<TResult>;

public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}

public interface IAuditoriaRepository
{
    void Agregar(RegistroAuditoria registro);
}

public interface IAuditoriaReadModel
{
    Task<IReadOnlyList<RegistroAuditoriaDto>> Buscar(CriterioAuditoria criterio, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IEventPublisher
{
    Task Publicar(string tipo, string payload, Guid eventId, CancellationToken cancellationToken);
}

public sealed record CriterioAuditoria(
    string CodigoEmpresa,
    string? Usuario,
    string? Entidad,
    DateOnly? FechaDesde,
    int? MaximoResultados);
