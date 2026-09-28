using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public sealed record AuthTokenPair(string Token, string RefreshToken, DateTimeOffset ExpiresAt);
public enum RefreshStatus { Success, Invalid, Replay }
public sealed record RefreshResult(RefreshStatus Status, AuthTokenPair? Pair = null, long? UserId = null);

public sealed class AuthSessionService(
    TicketsCombustibleDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IAuditoriaService auditoria,
    IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public async Task<AuthTokenPair> CreateAsync(Usuario user, IReadOnlyCollection<string> roles)
    {
        var now = timeProvider.GetUtcNow();
        var refresh = NewRefreshToken();
        db.SesionesUsuario.Add(new SesionUsuario
        {
            Id = Guid.NewGuid(), FamiliaId = Guid.NewGuid(), UsuarioId = user.Id,
            HashRefreshToken = Hash(refresh), CreadoEn = now, ExpiraEn = now.Add(RefreshLifetime)
        });
        var pair = new AuthTokenPair(CreateAccessToken(user, roles, now), refresh, now.Add(AccessLifetime));
        await auditoria.RegistrarAsync("LOGIN_EXITOSO", "USUARIO", user.Id.ToString(), "EXITO",
            datosNuevos: new { roles, accessExpiresAt = pair.ExpiresAt }, usuarioId: user.Id);
        return pair;
    }

    public async Task<RefreshResult> RefreshAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 43) return new(RefreshStatus.Invalid);
        var hash = Hash(token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var locked = await db.SesionesUsuario.FromSqlInterpolated($"SELECT * FROM sesiones_usuario WHERE hash_refresh_token = {hash} FOR UPDATE").SingleOrDefaultAsync();
        if (locked is null)
        {
            await transaction.CommitAsync();
            return new(RefreshStatus.Invalid);
        }

        var now = timeProvider.GetUtcNow();
        if (locked.RevocadoEn is not null)
        {
            var replay = locked.ReemplazadoPorId is not null;
            if (replay)
            {
                await db.SesionesUsuario.Where(x => x.FamiliaId == locked.FamiliaId && x.RevocadoEn == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevocadoEn, now));
                await auditoria.RegistrarAsync("REFRESH_REUSE_DETECTED", "SESION", locked.Id.ToString(), "FALLO",
                    detalle: "Se detectó reutilización de refresh token; la familia fue revocada.", usuarioId: locked.UsuarioId);
            }
            await transaction.CommitAsync();
            return new(replay ? RefreshStatus.Replay : RefreshStatus.Invalid, UserId: locked.UsuarioId);
        }
        if (locked.ExpiraEn <= now)
        {
            locked.RevocadoEn = now;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new(RefreshStatus.Invalid, UserId: locked.UsuarioId);
        }

        var user = await db.Usuarios.SingleOrDefaultAsync(x => x.Id == locked.UsuarioId && x.Activo);
        if (user is null)
        {
            await db.SesionesUsuario.Where(x => x.FamiliaId == locked.FamiliaId && x.RevocadoEn == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevocadoEn, now));
            await transaction.CommitAsync();
            return new(RefreshStatus.Invalid, UserId: locked.UsuarioId);
        }
        var roles = await (from ur in db.UsuarioRoles join role in db.Roles on ur.RolId equals role.Id
            where ur.UsuarioId == user.Id && role.Activo select role.Nombre).ToListAsync();
        var nextToken = NewRefreshToken();
        var replacement = new SesionUsuario
        {
            Id = Guid.NewGuid(), FamiliaId = locked.FamiliaId, UsuarioId = user.Id,
            HashRefreshToken = Hash(nextToken), CreadoEn = now, ExpiraEn = now.Add(RefreshLifetime)
        };
        db.SesionesUsuario.Add(replacement);
        await db.SaveChangesAsync();
        locked.RevocadoEn = now;
        locked.ReemplazadoPorId = replacement.Id;
        locked.UltimoUsoEn = now;
        var pair = new AuthTokenPair(CreateAccessToken(user, roles, now), nextToken, now.Add(AccessLifetime));
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("REFRESH", "SESION", replacement.Id.ToString(), "EXITO",
            datosNuevos: new { usuarioId = user.Id, expiraEn = replacement.ExpiraEn }, usuarioId: user.Id);
        await transaction.CommitAsync();
        return new(RefreshStatus.Success, pair, user.Id);
    }

    public async Task<bool> LogoutAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 43) return false;
        var hash = Hash(token);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var session = await db.SesionesUsuario.FromSqlInterpolated($"SELECT * FROM sesiones_usuario WHERE hash_refresh_token = {hash} FOR UPDATE").SingleOrDefaultAsync();
        if (session is null) { await transaction.CommitAsync(); return false; }
        var now = timeProvider.GetUtcNow();
        await db.SesionesUsuario.Where(x => x.FamiliaId == session.FamiliaId && x.RevocadoEn == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevocadoEn, now));
        await auditoria.RegistrarAsync("LOGOUT", "SESION", session.Id.ToString(), "EXITO", usuarioId: session.UsuarioId);
        await transaction.CommitAsync();
        return true;
    }

    public async Task RevokeAllAsync(long userId, string action = "REVOKE_ALL")
    {
        var now = timeProvider.GetUtcNow();
        await db.SesionesUsuario.Where(x => x.UsuarioId == userId && x.RevocadoEn == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevocadoEn, now));
        await auditoria.RegistrarAsync(action, "USUARIO", userId.ToString(), "EXITO", usuarioId: userId);
    }

    private string CreateAccessToken(Usuario user, IEnumerable<string> roles, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.NombreUsuario),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var issuer = configuration["Jwt:Issuer"] ?? "TicketsCombustible.Api";
        var audience = configuration["Jwt:Audience"] ?? "LaVomVa.Client";
        var token = new JwtSecurityToken(issuer, audience, claims, now.UtcDateTime, now.Add(AccessLifetime).UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string? ClientIp() => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    private static string NewRefreshToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
