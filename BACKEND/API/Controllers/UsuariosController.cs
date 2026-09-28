using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMINISTRADOR")]
[Route("api/gestion/usuarios")]
public class UsuariosController(TicketsCombustibleDbContext db, IAuditoriaService auditoria, AuthSessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var usuarios = await db.Usuarios.OrderBy(x => x.NombreUsuario).Select(x => new { x.Id, x.NombreUsuario, x.Correo, x.NombreCompleto, x.Telefono, x.Activo }).ToListAsync();
        var roles = await (from ur in db.UsuarioRoles join r in db.Roles on ur.RolId equals r.Id select new { ur.UsuarioId, ur.RolId, r.Nombre }).ToListAsync();
        return Ok(usuarios.Select(x => new { x.Id, x.NombreUsuario, x.Correo, x.NombreCompleto, x.Telefono, x.Activo,
            RolId = roles.FirstOrDefault(r => r.UsuarioId == x.Id)?.RolId,
            Rol = roles.FirstOrDefault(r => r.UsuarioId == x.Id)?.Nombre }));
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearUsuarioRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12) return BadRequest(new ApiErrorResponse("La contraseña debe tener al menos 12 caracteres."));
        if (request.NombreUsuario.Trim().Length < 3 || request.NombreCompleto.Trim().Length < 3 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Correo, out _)) return BadRequest(new ApiErrorResponse("Usuario, nombre o correo inválidos."));
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == request.NombreUsuario || x.Correo == request.Correo)) return Conflict(new ApiErrorResponse("El usuario o correo ya existe."));
        if (!await db.Roles.AnyAsync(x => x.Id == request.RolId && x.Activo)) return BadRequest(new ApiErrorResponse("El rol indicado no existe o está inactivo."));

        var usuario = new Usuario { NombreUsuario = request.NombreUsuario, Correo = request.Correo, NombreCompleto = request.NombreCompleto, Telefono = request.Telefono, PasswordHash = CrearHash(request.Password) };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = request.RolId });
        await db.SaveChangesAsync();
        var rol = await db.Roles.Where(x => x.Id == request.RolId).Select(x => x.Nombre).SingleAsync();
        await auditoria.RegistrarAsync("USER_CREATED", "USUARIO", usuario.Id.ToString(), "EXITO", datosNuevos: Snapshot(usuario, request.RolId, rol), usuarioId: usuario.Id);
        await transaction.CommitAsync();
        return Created($"api/gestion/usuarios/{usuario.Id}", new { usuario.Id, usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, usuario.Telefono, request.RolId });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, EditarUsuarioRequest request)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound(new ApiErrorResponse("Usuario no encontrado."));
        if (string.IsNullOrWhiteSpace(request.NombreCompleto) || !System.Net.Mail.MailAddress.TryCreate(request.Correo, out _)) return BadRequest(new ApiErrorResponse("Nombre o correo inválido."));
        if (await db.Usuarios.AnyAsync(x => x.Id != id && x.Correo == request.Correo)) return Conflict(new ApiErrorResponse("El correo ya existe."));
        if (!await db.Roles.AnyAsync(x => x.Id == request.RolId && x.Activo)) return BadRequest(new ApiErrorResponse("Rol inválido."));
        var rolesActuales = await db.UsuarioRoles.Where(x => x.UsuarioId == id).ToListAsync();
        var rolAnteriorId = rolesActuales.SingleOrDefault()?.RolId;
        var rolAnterior = rolAnteriorId is null ? null : await db.Roles.Where(x => x.Id == rolAnteriorId).Select(x => x.Nombre).SingleOrDefaultAsync();
        var anterior = Snapshot(usuario, rolAnteriorId, rolAnterior);
        if (rolesActuales.Count != 1 || rolesActuales[0].RolId != request.RolId)
        {
            var adminId = await db.Roles.Where(x => x.Nombre == "ADMINISTRADOR").Select(x => x.Id).SingleOrDefaultAsync();
            var esAdmin = rolesActuales.Any(x => x.RolId == adminId);
            var cuentaAdmins = await (from ur in db.UsuarioRoles join r in db.Roles on ur.RolId equals r.Id where r.Nombre == "ADMINISTRADOR" select ur.UsuarioId).Distinct().CountAsync();
            if (esAdmin && request.RolId != adminId && cuentaAdmins <= 1) return Conflict(new ApiErrorResponse("No se puede quitar el último administrador."));
            db.UsuarioRoles.RemoveRange(rolesActuales.Where(x => x.RolId != request.RolId));
            if (!rolesActuales.Any(x => x.RolId == request.RolId)) db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = id, RolId = request.RolId });
        }
        usuario.NombreCompleto = request.NombreCompleto.Trim();
        usuario.Correo = request.Correo.Trim().ToLowerInvariant();
        usuario.Telefono = request.Telefono;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        var nuevoRol = await db.Roles.Where(x => x.Id == request.RolId).Select(x => x.Nombre).SingleAsync();
        var nuevo = Snapshot(usuario, request.RolId, nuevoRol);
        await auditoria.RegistrarAsync("USER_UPDATED", "USUARIO", usuario.Id.ToString(), "EXITO", anterior, nuevo);
        if (rolAnteriorId != request.RolId)
        {
            await auditoria.RegistrarAsync("USER_ROLE_CHANGED", "USUARIO", usuario.Id.ToString(), "EXITO", new { rolId = rolAnteriorId, rol = rolAnterior }, new { rolId = request.RolId, rol = nuevoRol });
            await sessions.RevokeAllAsync(id, "USER_ROLE_SESSIONS_REVOKED");
        }
        await transaction.CommitAsync();
        return Ok(new { usuario.Id, usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, usuario.Telefono, request.RolId });
    }

    [HttpPost("{id:long}/restablecer-contrasena")]
    public async Task<IActionResult> RestablecerContrasena(long id, RestablecerContrasenaRequest request)
    {
        if (request.Contrasena.Length < 12) return BadRequest(new ApiErrorResponse("La contraseña debe tener al menos 12 caracteres."));
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound(new ApiErrorResponse("Usuario no encontrado."));
        await using var transaction = await db.Database.BeginTransactionAsync();
        usuario.PasswordHash = CrearHash(request.Contrasena);
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_PASSWORD_RESET", "USUARIO", usuario.Id.ToString(), "EXITO", detalle: "Contraseña restablecida; el valor no se registra.");
        await sessions.RevokeAllAsync(id, "USER_PASSWORD_SESSIONS_REVOKED");
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Desactivar(long id)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound(new ApiErrorResponse("Usuario no encontrado."));
        if (long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) && actorId == id) return Conflict(new ApiErrorResponse("No puedes desactivar tu propia cuenta."));
        var anterior = Snapshot(usuario);
        await using var transaction = await db.Database.BeginTransactionAsync();
        usuario.Activo = false;
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_DEACTIVATED", "USUARIO", usuario.Id.ToString(), "EXITO", anterior, Snapshot(usuario));
        await sessions.RevokeAllAsync(id, "USER_DISABLED_SESSIONS_REVOKED");
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/activar")]
    public async Task<IActionResult> Activar(long id)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound(new ApiErrorResponse("Usuario no encontrado."));
        var anterior = Snapshot(usuario);
        await using var transaction = await db.Database.BeginTransactionAsync();
        usuario.Activo = true;
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("USER_ACTIVATED", "USUARIO", usuario.Id.ToString(), "EXITO", anterior, Snapshot(usuario));
        await transaction.CommitAsync();
        return NoContent();
    }

    private static object Snapshot(Usuario usuario, long? rolId = null, string? rol = null) => new
    {
        usuario.Id,
        usuario.NombreUsuario,
        usuario.Correo,
        usuario.NombreCompleto,
        usuario.Telefono,
        usuario.Activo,
        rolId,
        rol
    };

    private static string CrearHash(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, 100_000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2$100000${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }
}

public record CrearUsuarioRequest(string NombreUsuario, string Correo, string NombreCompleto, string Password, string? Telefono, long RolId);
public record EditarUsuarioRequest(string Correo, string NombreCompleto, string? Telefono, long RolId);
public record RestablecerContrasenaRequest(string Contrasena);
