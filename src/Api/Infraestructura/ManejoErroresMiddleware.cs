using System.Diagnostics;
using CoreFid.Auditoria.Api.Contratos;
using CoreFid.Auditoria.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CoreFid.Auditoria.Api.Infraestructura;

public sealed class ManejoErroresMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ManejoErroresMiddleware> _logger;

    public ManejoErroresMiddleware(RequestDelegate next, ILogger<ManejoErroresMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            _logger.LogError(ex, "Error no controlado en {Metodo} {Ruta}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                RespuestaPlataforma<object>.Error(CodigosError.ErrorGeneral, "Error interno al procesar la solicitud.", $"traceId={traceId}"));
        }
    }

    public static IActionResult RespuestaModeloInvalido(ActionContext contexto)
    {
        var detalle = string.Join("; ", contexto.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value!.Errors.Select(e => e.ErrorMessage))}"));

        return new BadRequestObjectResult(
            RespuestaPlataforma<object>.Error(CodigosError.PeticionVacia, "Petición inválida.", string.IsNullOrEmpty(detalle) ? null : detalle));
    }
}
