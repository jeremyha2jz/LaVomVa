using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/login")]
public class AuthController(
    TicketsCombustibleDbContext db,
    IConfiguration configuration,
    IAuditoriaService auditoria,
    AuthSessionService sessions) : ControllerBase
{
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        NoStore();
        if (string.IsNullOrWhiteSpace(request.Usuario) || request.Usuario.Length > 80 || string.IsNullOrEmpty(request.Contrasena) || request.Contrasena.Length > 256)
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos" });
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.NombreUsuario == request.Usuario);
        if (usuario is null || !VerificarHash(request.Contrasena, usuario.PasswordHash))
        {
            await auditoria.RegistrarAsync("LOGIN", "USUARIO", usuario?.Id.ToString() ?? request.Usuario.Trim(), "FALLO", detalle: "Credenciales inválidas.", usuarioId: usuario?.Id);
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos" });
        }
        if (!usuario.Activo)
        {
            await auditoria.RegistrarAsync("LOGIN", "USUARIO", usuario.Id.ToString(), "FALLO", detalle: "Cuenta inactiva.", usuarioId: usuario.Id);
            return StatusCode(403, new { mensaje = "La cuenta espera activación o está desactivada." });
        }
        var roles = await CurrentRoles(usuario.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var pair = await sessions.CreateAsync(usuario, roles);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { token = pair.Token, refreshToken = pair.RefreshToken, expiresAt = pair.ExpiresAt, id = usuario.Id, nombre = usuario.NombreCompleto, rol = roles.FirstOrDefault() ?? "CONSULTA" });
    }

    [AllowAnonymous, HttpPost("refresh")]
    public async Task<IActionResult> Refresh(TokenRequest request)
    {
        NoStore();
        var result = await sessions.RefreshAsync(request.RefreshToken);
        if (result.Status != RefreshStatus.Success || result.Pair is null)
            return Unauthorized(new { mensaje = "La sesión no es válida. Inicia sesión nuevamente." });
        return Ok(new { token = result.Pair.Token, refreshToken = result.Pair.RefreshToken, expiresAt = result.Pair.ExpiresAt });
    }

    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout(TokenRequest request)
    {
        NoStore();
        await sessions.LogoutAsync(request.RefreshToken);
        return NoContent();
    }

    [Authorize, HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        if (!CurrentUserId(out var id)) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await sessions.RevokeAllAsync(id);
        await transaction.CommitAsync();
        return NoContent();
    }

    [Authorize, HttpPost("cambiar-contrasena")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        if (!CurrentUserId(out var id)) return Unauthorized();
        if (request.NuevaContrasena.Length < 12 || request.NuevaContrasena.Length > 256)
            return BadRequest(new { mensaje = "La contraseña nueva debe tener entre 12 y 256 caracteres." });
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.Id == id && x.Activo);
        if (usuario is null || !VerificarHash(request.ContrasenaActual, usuario.PasswordHash))
            return BadRequest(new { mensaje = "No se pudo cambiar la contraseña." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        usuario.PasswordHash = CrearHash(request.NuevaContrasena);
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_PASSWORD_CHANGED", "USUARIO", id.ToString(), "EXITO", detalle: "Contraseña cambiada; el valor no se registra.", usuarioId: id);
        await sessions.RevokeAllAsync(id);
        await transaction.CommitAsync();
        return NoContent();
    }

    [AllowAnonymous, HttpPost("registro")]
    public async Task<IActionResult> Registro(RegistroRequest request)
    {
        var nombreUsuario = request.Usuario.Trim();
        var correo = request.Correo.Trim().ToLowerInvariant();
        var nombre = request.NombreCompleto.Trim();
        if (nombreUsuario.Length is < 3 or > 80 || nombre.Length is < 3 or > 150 || request.Contrasena.Length is < 12 or > 256 ||
            !System.Net.Mail.MailAddress.TryCreate(correo, out var address) || address.Address != correo)
            return BadRequest(new { mensaje = "Indica usuario, nombre y correo válidos y una contraseña de al menos 12 caracteres." });
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == nombreUsuario || x.Correo == correo))
            return Conflict(new { mensaje = "El usuario o correo ya existe." });
        var rol = await db.Roles.SingleOrDefaultAsync(x => x.Nombre == "CONSULTA" && x.Activo);
        if (rol is null) return Problem("El rol CONSULTA debe existir antes de permitir registros.");
        var usuario = new Usuario { NombreUsuario = nombreUsuario, Correo = correo, NombreCompleto = nombre, Activo = false, PasswordHash = CrearHash(request.Contrasena) };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id });
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_REGISTERED", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: new { usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, usuario.Activo, rol = rol.Nombre }, usuarioId: usuario.Id);
        await transaction.CommitAsync();
        return Ok(new { mensaje = "Cuenta creada. Espera la activación de un administrador." });
    }

    [AllowAnonymous, HttpPost("inicializar-admin")]
    public async Task<IActionResult> InicializarAdmin(InicializarAdminRequest request)
    {
        var secret = configuration["Bootstrap:Secret"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return NotFound();
        var supplied = Encoding.UTF8.GetBytes(request.Secreto ?? "");
        var expected = Encoding.UTF8.GetBytes(secret);
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected)) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(540198217654321)");
        if (await db.UsuarioRoles.Join(db.Roles, x => x.RolId, x => x.Id, (ur, role) => new { ur, role }).AnyAsync(x => x.role.Nombre == "ADMINISTRADOR"))
            return Conflict(new { mensaje = "Ya existe un administrador." });
        var rolAdmin = await db.Roles.SingleOrDefaultAsync(x => x.Nombre == "ADMINISTRADOR" && x.Activo);
        if (rolAdmin is null) return Problem("El rol ADMINISTRADOR debe existir.");
        if (request.Usuario.Trim().Length is < 3 or > 80 || request.NombreCompleto.Trim().Length is < 3 or > 150 || request.Contrasena.Length is < 12 or > 256 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Correo.Trim(), out _)) return BadRequest(new { mensaje = "Datos de cuenta inválidos." });
        var nombreUsuario = request.Usuario.Trim(); var correo = request.Correo.Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == nombreUsuario || x.Correo == correo)) return Conflict(new { mensaje = "Usuario o correo existente." });
        var usuario = new Usuario { NombreUsuario = nombreUsuario, Correo = correo, NombreCompleto = request.NombreCompleto.Trim(), PasswordHash = CrearHash(request.Contrasena) };
        db.Usuarios.Add(usuario); await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rolAdmin.Id }); await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("ADMIN_BOOTSTRAPPED", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: new { usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, rol = rolAdmin.Nombre }, usuarioId: usuario.Id);
        await transaction.CommitAsync();
        return Ok(new { mensaje = "Administrador inicial creado." });
    }

    private async Task<List<string>> CurrentRoles(long userId) => await (from ur in db.UsuarioRoles join role in db.Roles on ur.RolId equals role.Id where ur.UsuarioId == userId && role.Activo select role.Nombre).OrderBy(x => x).ToListAsync();
    private bool CurrentUserId(out long id) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
    private void NoStore() => Response.Headers.CacheControl = "no-store, no-cache, max-age=0";

    private static string CrearHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    private static bool VerificarHash(string password, string almacenado)
    {
        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != "PBKDF2" || !int.TryParse(partes[1], out var iteraciones)) return false;
        try
        {
            if (iteraciones < 100_000 || iteraciones > 1_000_000) return false;
            var esperado = Convert.FromBase64String(partes[3]);
            var real = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(partes[2]), iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(real, esperado);
        }
        catch (FormatException) { return false; }
    }
}

public record LoginRequest(string Usuario, string Contrasena);
public record RegistroRequest(string Usuario, string Correo, string NombreCompleto, string Contrasena);
public record InicializarAdminRequest(string Secreto, string Usuario, string Correo, string NombreCompleto, string Contrasena);
public record TokenRequest(string? RefreshToken);
public record ChangePasswordRequest(string ContrasenaActual, string NuevaContrasena);
