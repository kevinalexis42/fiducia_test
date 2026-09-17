using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoreFid.Auditoria.Tests.Integration.Soporte;

public static class TokenDePrueba
{
    public const string Issuer = "https://sts.windows.net/eval-tenant/";
    public const string Audience = "api://corefid-audit";
    public const string Secret = "eval-secret-please-change-me-0123456789abcdef";

    public static string Generar(
        string[]? roles = null,
        string usuario = "jperez",
        string? issuer = null,
        string? audience = null,
        string? secret = null,
        DateTime? expira = null)
    {
        roles ??= new[] { "audit.write", "audit.read" };
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? Secret));
        var credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "b3f1a2c4-1111-2222-3333-444455556666"),
            new("preferred_username", usuario)
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: (expira ?? DateTime.UtcNow).AddHours(-2),
            expires: expira ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
