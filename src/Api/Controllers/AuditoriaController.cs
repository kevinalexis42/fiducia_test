using CoreFid.Auditoria.Api.Contratos;
using CoreFid.Auditoria.Api.Infraestructura;
using CoreFid.Auditoria.Application.Abstractions;
using CoreFid.Auditoria.Application.Auditoria.Commands;
using CoreFid.Auditoria.Application.Auditoria.Dtos;
using CoreFid.Auditoria.Application.Auditoria.Queries;
using CoreFid.Auditoria.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoreFid.Auditoria.Api.Controllers;

[ApiController]
[Route("api/auditoria")]
[Produces("application/json")]
public sealed class AuditoriaController : ControllerBase
{
    private readonly ICommandHandler<RegistrarAuditoriaCommand, long> _registrar;
    private readonly IQueryHandler<ConsultarAuditoriaQuery, IReadOnlyList<RegistroAuditoriaDto>> _consultar;

    public AuditoriaController(
        ICommandHandler<RegistrarAuditoriaCommand, long> registrar,
        IQueryHandler<ConsultarAuditoriaQuery, IReadOnlyList<RegistroAuditoriaDto>> consultar)
    {
        _registrar = registrar;
        _consultar = consultar;
    }

    [HttpPost("registro")]
    [Authorize(Policy = Politicas.Escritura)]
    [ProducesResponseType(typeof(RespuestaPlataforma<long?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RespuestaPlataforma<long?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RespuestaPlataforma<long?>>> Registrar(
        [FromBody] RegistroAuditoriaRequest request, CancellationToken cancellationToken)
    {
        var command = new RegistrarAuditoriaCommand(
            request.CodigoEmpresa,
            request.CodigoModulo,
            request.CodigoTransaccion,
            request.Usuario,
            request.Entidad,
            request.ClaveEntidad,
            request.Accion,
            request.ValorAnterior,
            request.ValorNuevo,
            request.DireccionIp,
            request.Canal);

        try
        {
            var id = await _registrar.Handle(command, cancellationToken);
            return Ok(RespuestaPlataforma<long?>.Ok("Registro creado.", id));
        }
        catch (DomainException ex) when (ex.Codigo == CodigosError.BatchNoRegistraConsultas)
        {
            return BadRequest(RespuestaPlataforma<long?>.Error(ex.Codigo, ex.Message));
        }
        catch (DomainException ex)
        {
            return Ok(RespuestaPlataforma<long?>.Error(ex.Codigo, ex.Message));
        }
    }

    [HttpGet("consulta")]
    [Authorize(Policy = Politicas.Lectura)]
    [ProducesResponseType(typeof(RespuestaPlataforma<IReadOnlyList<RegistroAuditoriaDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RespuestaPlataforma<IReadOnlyList<RegistroAuditoriaDto>>>> Consultar(
        [FromQuery] string? codigoEmpresa,
        [FromQuery] string? usuario,
        [FromQuery] string? entidad,
        [FromQuery] DateOnly? fechaDesde,
        CancellationToken cancellationToken)
    {
        try
        {
            var resultado = await _consultar.Handle(
                new ConsultarAuditoriaQuery(codigoEmpresa, usuario, entidad, fechaDesde), cancellationToken);

            return Ok(RespuestaPlataforma<IReadOnlyList<RegistroAuditoriaDto>>.Ok("Consulta exitosa", resultado));
        }
        catch (DomainException ex)
        {
            return BadRequest(RespuestaPlataforma<IReadOnlyList<RegistroAuditoriaDto>>.Error(ex.Codigo, ex.Message));
        }
    }
}
