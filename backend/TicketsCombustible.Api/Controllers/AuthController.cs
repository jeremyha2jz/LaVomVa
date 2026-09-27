using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Data;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/login")]
public class AuthController(TicketsCombustibleDbContext db, IConfiguration configuration, IAuditoriaService auditoria) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request)
    {
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
        var roles = await (from ur in db.UsuarioRoles join r in db.Roles on ur.RolId equals r.Id where ur.UsuarioId == usuario.Id && r.Activo select r.Nombre).ToListAsync();
        roles = roles.OrderBy(x => x == "ADMINISTRADOR" ? 0 : x == "SUPERVISOR" ? 1 : x == "DESPACHADOR" ? 2 : x == "SOLICITANTE" ? 3 : 4).ToList();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, usuario.Id.ToString()), new(ClaimTypes.Name, usuario.NombreUsuario) };
        claims.AddRange(roles.Select(rol => new Claim(ClaimTypes.Role, rol)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        await auditoria.RegistrarAsync("LOGIN", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: new { roles }, detalle: "Inicio de sesión exitoso.", usuarioId: usuario.Id);
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), id = usuario.Id, nombre = usuario.NombreCompleto, rol = roles.FirstOrDefault() ?? "CONSULTA" });
    }

    [HttpPost("registro")]
    public async Task<IActionResult> Registro(RegistroRequest request)
    {
        var nombreUsuario = request.Usuario.Trim();
        var correo = request.Correo.Trim().ToLowerInvariant();
        var nombre = request.NombreCompleto.Trim();
        if (nombreUsuario.Length is < 3 or > 80 || nombre.Length is < 3 or > 150 ||
            !System.Net.Mail.MailAddress.TryCreate(correo, out var address) || address.Address != correo ||
            request.Contrasena.Length < 12)
            return BadRequest(new { mensaje = "Indica usuario, nombre y correo válidos y una contraseña de al menos 12 caracteres." });
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == nombreUsuario || x.Correo == correo))
            return Conflict(new { mensaje = "El usuario o correo ya existe." });
        var rol = await db.Roles.SingleOrDefaultAsync(x => x.Nombre == "CONSULTA" && x.Activo);
        if (rol is null) return Problem("El rol CONSULTA debe existir antes de permitir registros.");
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Contrasena, sal, 100_000, HashAlgorithmName.SHA256, 32);
        var usuario = new Usuario { NombreUsuario = nombreUsuario, Correo = correo, NombreCompleto = nombre, Activo = false,
            PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}" };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id });
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_REGISTERED", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: new { usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, usuario.Activo, rol = rol.Nombre }, usuarioId: usuario.Id);
        await transaction.CommitAsync();
        return Ok(new { mensaje = "Cuenta creada. Espera la activación de un administrador." });
    }

    [HttpPost("inicializar-admin")]
    public async Task<IActionResult> InicializarAdmin(InicializarAdminRequest request)
    {
        var secret = configuration["Bootstrap:Secret"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return NotFound();
        var supplied = Encoding.UTF8.GetBytes(request.Secreto ?? "");
        var expected = Encoding.UTF8.GetBytes(secret);
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected)) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (await db.UsuarioRoles.Join(db.Roles, x => x.RolId, x => x.Id, (ur, rol) => new { ur, rol })
            .AnyAsync(x => x.rol.Nombre == "ADMINISTRADOR")) return Conflict(new { mensaje = "Ya existe un administrador." });
        var rolAdmin = await db.Roles.SingleOrDefaultAsync(x => x.Nombre == "ADMINISTRADOR" && x.Activo);
        if (rolAdmin is null) return Problem("El rol ADMINISTRADOR debe existir.");
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == request.Usuario || x.Correo == request.Correo)) return Conflict(new { mensaje = "Usuario o correo existente." });
        if (request.Contrasena.Length < 12 || request.Usuario.Trim().Length < 3 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Correo, out _)) return BadRequest(new { mensaje = "Datos de cuenta inválidos." });
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Contrasena, sal, 100_000, HashAlgorithmName.SHA256, 32);
        var usuario = new Usuario { NombreUsuario = request.Usuario.Trim(), Correo = request.Correo.Trim().ToLowerInvariant(),
            NombreCompleto = request.NombreCompleto.Trim(), PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}" };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rolAdmin.Id });
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("ADMIN_BOOTSTRAPPED", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: new { usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, rol = rolAdmin.Nombre }, usuarioId: usuario.Id);
        await transaction.CommitAsync();
        return Ok(new { mensaje = "Administrador inicial creado." });
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
