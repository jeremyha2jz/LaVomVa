using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
[Route("api/gestion")]
public class GestionCatalogosController(TicketsCombustibleDbContext db, IAuditoriaService auditoria) : ControllerBase
{
    [HttpPost("departamentos")]
    public async Task<IActionResult> CrearDepartamento(Departamento item)
    {
        if (string.IsNullOrWhiteSpace(item.Nombre)) return BadRequest("El nombre del departamento es obligatorio.");
        if (await db.Departamentos.AnyAsync(x => x.Nombre == item.Nombre || (item.Codigo != null && x.Codigo == item.Codigo))) return Conflict("El nombre o código del departamento ya existe.");
        db.Departamentos.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "DEPARTAMENTO", item.Id.ToString(), "EXITO", datosNuevos: item); await tx.CommitAsync(); return Created($"api/gestion/departamentos/{item.Id}", item);
    }

    [HttpPut("departamentos/{id:long}")]
    public async Task<IActionResult> EditarDepartamento(long id, Departamento entrada) { var item = await db.Departamentos.FindAsync(id); if (item is null) return NotFound(); if (string.IsNullOrWhiteSpace(entrada.Nombre)) return BadRequest("El nombre del departamento es obligatorio."); if (await db.Departamentos.AnyAsync(x => x.Id != id && (x.Nombre == entrada.Nombre || (entrada.Codigo != null && x.Codigo == entrada.Codigo)))) return Conflict("El nombre o código del departamento ya existe."); var before = Snapshot(item); item.Codigo = entrada.Codigo; item.Nombre = entrada.Nombre; item.Descripcion = entrada.Descripcion; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "DEPARTAMENTO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(item); }

    [HttpPost("empleados")]
    public async Task<IActionResult> CrearEmpleado(Empleado item) { if (string.IsNullOrWhiteSpace(item.CodigoEmpleado) || string.IsNullOrWhiteSpace(item.NombreCompleto) || string.IsNullOrWhiteSpace(item.Cedula)) return BadRequest("Código, nombre y cédula son obligatorios."); if (!CorreoValido(item.Correo)) return BadRequest("El correo del empleado no es válido."); if (!await db.Departamentos.AnyAsync(x => x.Id == item.DepartamentoId && x.Activo)) return BadRequest("El departamento no existe o está inactivo."); if (await db.Empleados.AnyAsync(x => x.CodigoEmpleado == item.CodigoEmpleado || x.Cedula == item.Cedula)) return Conflict("El código o cédula del empleado ya existe."); db.Empleados.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "EMPLEADO", item.Id.ToString(), "EXITO", datosNuevos: item); await tx.CommitAsync(); return Created($"api/gestion/empleados/{item.Id}", item); }

    [HttpPut("empleados/{id:long}")]
    public async Task<IActionResult> EditarEmpleado(long id, Empleado entrada) { var item = await db.Empleados.FindAsync(id); if (item is null) return NotFound(); if (string.IsNullOrWhiteSpace(entrada.CodigoEmpleado) || string.IsNullOrWhiteSpace(entrada.NombreCompleto) || string.IsNullOrWhiteSpace(entrada.Cedula)) return BadRequest("Código, nombre y cédula son obligatorios."); if (!CorreoValido(entrada.Correo)) return BadRequest("El correo del empleado no es válido."); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest("Departamento inválido."); if (await db.Empleados.AnyAsync(x => x.Id != id && (x.CodigoEmpleado == entrada.CodigoEmpleado || x.Cedula == entrada.Cedula))) return Conflict("El código o cédula del empleado ya existe."); var before = Snapshot(item); item.CodigoEmpleado = entrada.CodigoEmpleado; item.NombreCompleto = entrada.NombreCompleto; item.Cedula = entrada.Cedula; item.DepartamentoId = entrada.DepartamentoId; item.Cargo = entrada.Cargo; item.Correo = entrada.Correo; item.TelefonoMovil = entrada.TelefonoMovil; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "EMPLEADO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(item); }

    [HttpPost("vehiculos")]
    public async Task<IActionResult> CrearVehiculo(Vehiculo item) { var validation = ValidarVehiculo(item); if (validation is not null) return BadRequest(validation); if (!await db.Departamentos.AnyAsync(x => x.Id == item.DepartamentoId && x.Activo)) return BadRequest("El departamento no existe o está inactivo."); if (await db.Vehiculos.AnyAsync(x => x.Placa == item.Placa || x.Ficha == item.Ficha)) return Conflict("La placa o ficha del vehículo ya existe."); db.Vehiculos.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "VEHICULO", item.Id.ToString(), "EXITO", datosNuevos: item); await tx.CommitAsync(); return Created($"api/gestion/vehiculos/{item.Id}", item); }

    [HttpPut("vehiculos/{id:long}")]
    public async Task<IActionResult> EditarVehiculo(long id, Vehiculo entrada) { var item = await db.Vehiculos.FindAsync(id); if (item is null) return NotFound(); var validation = ValidarVehiculo(entrada); if (validation is not null) return BadRequest(validation); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest("Departamento inválido."); if (await db.Vehiculos.AnyAsync(x => x.Id != id && (x.Placa == entrada.Placa || x.Ficha == entrada.Ficha))) return Conflict("La placa o ficha del vehículo ya existe."); var before = Snapshot(item); item.Placa = entrada.Placa; item.Ficha = entrada.Ficha; item.Marca = entrada.Marca; item.Modelo = entrada.Modelo; item.Anio = entrada.Anio; item.Tipo = entrada.Tipo; item.DepartamentoId = entrada.DepartamentoId; item.CapacidadTanqueGalones = entrada.CapacidadTanqueGalones; item.OdometroKm = entrada.OdometroKm; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "VEHICULO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(item); }

    [HttpPost("estaciones")]
    public async Task<IActionResult> CrearEstacion(Estacion item) { db.Estaciones.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "ESTACION", item.Id.ToString(), "EXITO", datosNuevos: item); await tx.CommitAsync(); return Created($"api/gestion/estaciones/{item.Id}", item); }

    [HttpPost("tanques")]
    public async Task<IActionResult> CrearTanque(Tanque item) { if (!await db.Estaciones.AnyAsync(x => x.Id == item.EstacionId && x.Activo)) return BadRequest("La estación no existe o está inactiva."); if (!await db.TiposCombustible.AnyAsync(x => x.Id == item.TipoCombustibleId && x.Activo)) return BadRequest("Tipo de combustible inválido."); if (item.CapacidadGalones <= 0 || item.NivelCriticoGalones < 0 || item.NivelCriticoGalones > item.CapacidadGalones || item.ExistenciaActualGalones != 0) return BadRequest("El tanque nuevo debe iniciar vacío, con capacidad positiva y nivel crítico válido."); db.Tanques.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "TANQUE", item.Id.ToString(), "EXITO", datosNuevos: item); await tx.CommitAsync(); return Created($"api/gestion/tanques/{item.Id}", item); }

    [HttpDelete("{tipo}/{id:long}")]
    public async Task<IActionResult> Desactivar(string tipo, long id)
    {
        object? before = null;
        object? after = null;
        string? entity = null;
        switch (tipo.ToLowerInvariant())
        {
            case "departamentos": { var x = await db.Departamentos.FindAsync(id); if (x is null) return NotFound(); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "DEPARTAMENTO"; break; }
            case "empleados": { var x = await db.Empleados.FindAsync(id); if (x is null) return NotFound(); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "EMPLEADO"; break; }
            case "vehiculos": { var x = await db.Vehiculos.FindAsync(id); if (x is null) return NotFound(); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "VEHICULO"; break; }
            case "estaciones": { var x = await db.Estaciones.FindAsync(id); if (x is null) return NotFound(); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "ESTACION"; break; }
            case "tanques": { var x = await db.Tanques.FindAsync(id); if (x is null) return NotFound(); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "TANQUE"; break; }
            default: return BadRequest("Tipo no válido.");
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("CATALOG_DEACTIVATED", entity!, id.ToString(), "EXITO", before, after);
        await tx.CommitAsync();
        return NoContent();
    }

    private static object Snapshot(Departamento item) => new { item.Id, item.Codigo, item.Nombre, item.Descripcion, item.Activo };
    private static object Snapshot(Empleado item) => new { item.Id, item.CodigoEmpleado, item.NombreCompleto, item.DepartamentoId, item.Cargo, item.Activo };
    private static object Snapshot(Vehiculo item) => new { item.Id, item.Placa, item.Ficha, item.Marca, item.Modelo, item.Anio, item.DepartamentoId, item.CapacidadTanqueGalones, item.OdometroKm, item.Activo };
    private static object Snapshot(Estacion item) => new { item.Id, item.Nombre, item.Ubicacion, item.Activo };
    private static object Snapshot(Tanque item) => new { item.Id, item.Codigo, item.Nombre, item.EstacionId, item.TipoCombustibleId, item.CapacidadGalones, item.ExistenciaActualGalones, item.Activo };

    private static bool CorreoValido(string? correo) => string.IsNullOrWhiteSpace(correo) || System.Net.Mail.MailAddress.TryCreate(correo, out var address) && address.Address == correo;
    private static string? ValidarVehiculo(Vehiculo item)
    {
        if (string.IsNullOrWhiteSpace(item.Placa) || string.IsNullOrWhiteSpace(item.Ficha) || string.IsNullOrWhiteSpace(item.Marca) || string.IsNullOrWhiteSpace(item.Modelo)) return "Placa, ficha, marca y modelo son obligatorios.";
        if (item.Anio is < 1900 or > 2200) return "El año del vehículo debe estar entre 1900 y 2200.";
        if (item.CapacidadTanqueGalones is <= 0) return "La capacidad del tanque debe ser mayor que cero.";
        if (item.OdometroKm < 0) return "El odómetro no puede ser negativo.";
        return null;
    }
}
