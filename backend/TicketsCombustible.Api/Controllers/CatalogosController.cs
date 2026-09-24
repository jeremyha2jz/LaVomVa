using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/catalogos")]
public class CatalogosController(TicketsCombustibleDbContext db) : ControllerBase
{
    [HttpGet("departamentos")] public async Task<IActionResult> Departamentos() => Ok(await db.Departamentos.Where(x => x.Activo).OrderBy(x => x.Nombre).ToListAsync());
    [HttpGet("empleados")] public async Task<IActionResult> Empleados() => Ok(await db.Empleados.Where(x => x.Activo).OrderBy(x => x.NombreCompleto).ToListAsync());
    [HttpGet("vehiculos")] public async Task<IActionResult> Vehiculos() => Ok(await db.Vehiculos.Where(x => x.Activo).OrderBy(x => x.Placa).ToListAsync());
    [HttpGet("tipos-combustible")] public async Task<IActionResult> TiposCombustible() => Ok(await db.TiposCombustible.Where(x => x.Activo).OrderBy(x => x.Nombre).ToListAsync());
    [HttpGet("tanques")] public async Task<IActionResult> Tanques() => Ok(await db.Tanques.Where(x => x.Activo).OrderBy(x => x.Codigo).ToListAsync());
    [HttpGet("estaciones")] public async Task<IActionResult> Estaciones() => Ok(await db.Estaciones.Where(x => x.Activo).OrderBy(x => x.Nombre).ToListAsync());
    [HttpGet("roles")] public async Task<IActionResult> Roles() => Ok(await db.Roles.Where(x => x.Activo).OrderBy(x => x.Id).ToListAsync());
}
