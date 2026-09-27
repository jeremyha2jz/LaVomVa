using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/gestion/usuarios")]
public class UsuariosController(TicketsCombustibleDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar() => Ok(await db.Usuarios.OrderBy(x => x.NombreUsuario).Select(x => new { x.Id, x.NombreUsuario, x.Correo, x.NombreCompleto, x.Telefono, x.Activo }).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Crear(CrearUsuarioRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8) return BadRequest(new ApiErrorResponse("La contraseña debe tener al menos 8 caracteres."));
        if (await db.Usuarios.AnyAsync(x => x.NombreUsuario == request.NombreUsuario || x.Correo == request.Correo)) return Conflict(new ApiErrorResponse("El usuario o correo ya existe."));
        if (!await db.Roles.AnyAsync(x => x.Id == request.RolId && x.Activo)) return BadRequest(new ApiErrorResponse("El rol indicado no existe o está inactivo."));

        var usuario = new Usuario { NombreUsuario = request.NombreUsuario, Correo = request.Correo, NombreCompleto = request.NombreCompleto, Telefono = request.Telefono, PasswordHash = CrearHash(request.Password) };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = request.RolId });
        await db.SaveChangesAsync();
        return Created($"api/gestion/usuarios/{usuario.Id}", new { usuario.Id, usuario.NombreUsuario, usuario.Correo, usuario.NombreCompleto, usuario.Telefono, request.RolId });
    }

    private static string CrearHash(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, 100_000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2$100000${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }
}

public record CrearUsuarioRequest(string NombreUsuario, string Correo, string NombreCompleto, string Password, string? Telefono, long RolId);
