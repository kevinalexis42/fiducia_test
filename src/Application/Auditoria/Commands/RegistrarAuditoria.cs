using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Domain.Auditoria;
using Microsoft.Extensions.Logging;

namespace CoreFid.Auditoria.Application.Auditoria.Commands;

public sealed record RegistrarAuditoriaCommand(
    string? CodigoEmpresa,
    string? CodigoModulo,
    string? CodigoTransaccion,
    string? Usuario,
    string? Entidad,
    string? ClaveEntidad,
    string? Accion,
    string? ValorAnterior,
    string? ValorNuevo,
    string? DireccionIp,
    string? Canal) : ICommand<long>;

public sealed class RegistrarAuditoriaHandler : ICommandHandler<RegistrarAuditoriaCommand, long>
{
    private readonly IAuditoriaRepository _repositorio;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _reloj;
    private readonly ILogger<RegistrarAuditoriaHandler> _logger;

    public RegistrarAuditoriaHandler(
        IAuditoriaRepository repositorio,
        IUnitOfWork unitOfWork,
        TimeProvider reloj,
        ILogger<RegistrarAuditoriaHandler> logger)
    {
        _repositorio = repositorio;
        _unitOfWork = unitOfWork;
        _reloj = reloj;
        _logger = logger;
    }

    public async Task<long> Handle(RegistrarAuditoriaCommand command, CancellationToken cancellationToken)
    {
        var registro = RegistroAuditoria.Registrar(
            command.CodigoEmpresa,
            command.CodigoModulo,
            command.CodigoTransaccion,
            command.Usuario,
            command.Entidad,
            command.ClaveEntidad,
            command.Accion,
            command.ValorAnterior,
            command.ValorNuevo,
            command.DireccionIp,
            command.Canal,
            _reloj.GetUtcNow());

        _repositorio.Agregar(registro);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Auditoría registrada {AuditoriaId} empresa={CodigoEmpresa} entidad={Entidad} accion={Accion} sensible={EsSensible}",
            registro.Id, registro.CodigoEmpresa.Valor, registro.Entidad, registro.Accion.Valor, registro.EsAccionSensible);

        return registro.Id;
    }
}
