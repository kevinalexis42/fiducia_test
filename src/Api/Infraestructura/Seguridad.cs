using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CoreFid.Auditoria.Api.Infraestructura;

public static class Politicas
{
    public const string Lectura = "audit.read";
    public const string Escritura = "audit.write";
}

public sealed class JwtOptions
{
    public const string Seccion = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? SymmetricKey { get; set; }
    public string? Authority { get; set; }
    public bool RequireHttpsMetadata { get; set; } = true;
    public string RolesClaim { get; set; } = "roles";
}

public static class SeguridadExtensions
{
    public static IServiceCollection AddSeguridadJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Seccion).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
            throw new InvalidOperationException("Jwt:Issuer y Jwt:Audience son obligatorios.");

        if (string.IsNullOrWhiteSpace(jwt.SymmetricKey) && string.IsNullOrWhiteSpace(jwt.Authority))
            throw new InvalidOperationException("Configura Jwt:SymmetricKey (HS256, desarrollo) o Jwt:Authority (JWKS, producción).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = jwt.RolesClaim,
                    NameClaimType = "preferred_username"
                };

                if (!string.IsNullOrWhiteSpace(jwt.SymmetricKey))
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SymmetricKey));
                    options.TokenValidationParameters.ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 };
                }
                else
                {
                    options.Authority = jwt.Authority;
                    options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                    options.TokenValidationParameters.ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 };
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Lectura, p => p.RequireAuthenticatedUser().RequireRole(Politicas.Lectura))
            .AddPolicy(Politicas.Escritura, p => p.RequireAuthenticatedUser().RequireRole(Politicas.Escritura));

        return services;
    }
}
