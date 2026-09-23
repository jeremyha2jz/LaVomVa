using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/gestion")]
public class GestionCatalogosController(TicketsCombustibleDbContext db) : ControllerBase
{
    [HttpPost("departamentos")]
    public async Task<IActionResult> CrearDepartamento(Departamento item) { db.Departamentos.Add(item); await db.SaveChangesAsync(); return Created($"api/gestion/departamentos/{item.Id}", item); }

    [HttpPut("departamentos/{id:long}")]
    public async Task<IActionResult> EditarDepartamento(long id, Departamento entrada) { var item = await db.Departamentos.FindAsync(id); if (item is null) return NotFound(); item.Codigo = entrada.Codigo; item.Nombre = entrada.Nombre; item.Descripcion = entrada.Descripcion; item.Activo = entrada.Activo; await db.SaveChangesAsync(); return Ok(item); }

    [HttpPost("empleados")]
    public async Task<IActionResult> CrearEmpleado(Empleado item) { if (!await db.Departamentos.AnyAsync(x => x.Id == item.DepartamentoId && x.Activo)) return BadRequest("El departamento no existe o está inactivo."); db.Empleados.Add(item); await db.SaveChangesAsync(); return Created($"api/gestion/empleados/{item.Id}", item); }

    [HttpPut("empleados/{id:long}")]
    public async Task<IActionResult> EditarEmpleado(long id, Empleado entrada) { var item = await db.Empleados.FindAsync(id); if (item is null) return NotFound(); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest("Departamento inválido."); item.CodigoEmpleado = entrada.CodigoEmpleado; item.NombreCompleto = entrada.NombreCompleto; item.Cedula = entrada.Cedula; item.DepartamentoId = entrada.DepartamentoId; item.Cargo = entrada.Cargo; item.Correo = entrada.Correo; item.TelefonoMovil = entrada.TelefonoMovil; item.Activo = entrada.Activo; await db.SaveChangesAsync(); return Ok(item); }

    [HttpPost("vehiculos")]
    public async Task<IActionResult> CrearVehiculo(Vehiculo item) { if (!await db.Departamentos.AnyAsync(x => x.Id == item.DepartamentoId && x.Activo)) return BadRequest("El departamento no existe o está inactivo."); db.Vehiculos.Add(item); await db.SaveChangesAsync(); return Created($"api/gestion/vehiculos/{item.Id}", item); }

    [HttpPut("vehiculos/{id:long}")]
    public async Task<IActionResult> EditarVehiculo(long id, Vehiculo entrada) { var item = await db.Vehiculos.FindAsync(id); if (item is null) return NotFound(); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest("Departamento inválido."); item.Placa = entrada.Placa; item.Ficha = entrada.Ficha; item.Marca = entrada.Marca; item.Modelo = entrada.Modelo; item.Anio = entrada.Anio; item.Tipo = entrada.Tipo; item.DepartamentoId = entrada.DepartamentoId; item.CapacidadTanqueGalones = entrada.CapacidadTanqueGalones; item.OdometroKm = entrada.OdometroKm; item.Activo = entrada.Activo; await db.SaveChangesAsync(); return Ok(item); }

    [HttpPost("estaciones")]
    public async Task<IActionResult> CrearEstacion(Estacion item) { db.Estaciones.Add(item); await db.SaveChangesAsync(); return Created($"api/gestion/estaciones/{item.Id}", item); }

    [HttpPost("tanques")]
    public async Task<IActionResult> CrearTanque(Tanque item) { if (!await db.Estaciones.AnyAsync(x => x.Id == item.EstacionId && x.Activo)) return BadRequest("La estación no existe o está inactiva."); if (!await db.TiposCombustible.AnyAsync(x => x.Id == item.TipoCombustibleId && x.Activo)) return BadRequest("Tipo de combustible inválido."); if (item.CapacidadGalones <= 0) return BadRequest("La capacidad debe ser mayor que cero."); db.Tanques.Add(item); await db.SaveChangesAsync(); return Created($"api/gestion/tanques/{item.Id}", item); }

    [HttpDelete("{tipo}/{id:long}")]
    public async Task<IActionResult> Desactivar(string tipo, long id)
    {
        switch (tipo.ToLowerInvariant())
        {
            case "departamentos": { var x = await db.Departamentos.FindAsync(id); if (x is null) return NotFound(); x.Activo = false; break; }
            case "empleados": { var x = await db.Empleados.FindAsync(id); if (x is null) return NotFound(); x.Activo = false; break; }
            case "vehiculos": { var x = await db.Vehiculos.FindAsync(id); if (x is null) return NotFound(); x.Activo = false; break; }
            case "estaciones": { var x = await db.Estaciones.FindAsync(id); if (x is null) return NotFound(); x.Activo = false; break; }
            case "tanques": { var x = await db.Tanques.FindAsync(id); if (x is null) return NotFound(); x.Activo = false; break; }
            default: return BadRequest("Tipo no válido.");
        }
        await db.SaveChangesAsync(); return NoContent();
    }
}
