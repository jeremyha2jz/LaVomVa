using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TicketsCombustible.Api.Data;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/login")]
public class AuthController(TicketsCombustibleDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.NombreUsuario == request.Usuario && x.Activo);
        if (usuario is null || !VerificarHash(request.Contrasena, usuario.PasswordHash)) return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos" });
        var roles = await (from ur in db.UsuarioRoles join r in db.Roles on ur.RolId equals r.Id where ur.UsuarioId == usuario.Id && r.Activo select r.Nombre).ToListAsync();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, usuario.Id.ToString()), new(ClaimTypes.Name, usuario.NombreUsuario) };
        claims.AddRange(roles.Select(rol => new Claim(ClaimTypes.Role, rol)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), id = usuario.Id, nombre = usuario.NombreCompleto, rol = roles.FirstOrDefault() ?? "CONSULTA" });
    }

    private static bool VerificarHash(string password, string almacenado)
    {
        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != "PBKDF2" || !int.TryParse(partes[1], out var iteraciones)) return false;
        var esperado = Convert.FromBase64String(partes[3]);
        var real = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(partes[2]), iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(real, esperado);
    }
}

public record LoginRequest(string Usuario, string Contrasena);
