using CoreFid.Auditoria.Api.Contratos;
using CoreFid.Auditoria.Application.Auditoria.Dtos;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CoreFid.Auditoria.Api.Infraestructura;

public sealed class SwaggerContrato : ISchemaFilter
{
    public static string NombreDeEsquema(Type tipo)
    {
        if (tipo == typeof(RespuestaPlataforma<long?>)) return "ResponseCreateLong";
        if (tipo == typeof(RespuestaPlataforma<IReadOnlyList<RegistroAuditoriaDto>>)) return "ResponseCreateList";
        if (tipo == typeof(RegistroAuditoriaDto)) return "RegistroAuditoria";
        return tipo.Name;
    }

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(RegistroAuditoriaRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["codigoEmpresa"] = new OpenApiString("0001"),
                ["codigoModulo"] = new OpenApiString("06"),
                ["codigoTransaccion"] = new OpenApiString("V062014"),
                ["usuario"] = new OpenApiString("jperez"),
                ["entidad"] = new OpenApiString("SolicitudRescate"),
                ["claveEntidad"] = new OpenApiString("RES-2026-004871"),
                ["accion"] = new OpenApiString("U"),
                ["valorAnterior"] = new OpenApiString("{\"estado\":\"PENDIENTE\"}"),
                ["valorNuevo"] = new OpenApiString("{\"estado\":\"APROBADA\"}"),
                ["direccionIp"] = new OpenApiString("10.0.4.77"),
                ["canal"] = new OpenApiString("API")
            };
        }

        if (context.Type == typeof(RespuestaPlataforma<long?>))
        {
            schema.Example = new OpenApiObject
            {
                ["codigo"] = new OpenApiString("OK"),
                ["mensaje"] = new OpenApiString("Registro creado."),
                ["detalle"] = new OpenApiNull(),
                ["data"] = new OpenApiLong(884213)
            };
        }
    }
}
