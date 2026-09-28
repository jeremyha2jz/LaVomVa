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
    public async Task<IActionResult> CrearDepartamento(DepartamentoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre)) return BadRequest(new ApiErrorResponse("El nombre del departamento es obligatorio."));
        if (await db.Departamentos.AnyAsync(x => x.Nombre == request.Nombre || (request.Codigo != null && x.Codigo == request.Codigo))) return Conflict(new ApiErrorResponse("El nombre o código del departamento ya existe."));
        var item = new Departamento { Codigo = request.Codigo, Nombre = request.Nombre, Descripcion = request.Descripcion, Activo = request.Activo };
        db.Departamentos.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "DEPARTAMENTO", item.Id.ToString(), "EXITO", datosNuevos: Snapshot(item)); await tx.CommitAsync(); return Created($"api/gestion/departamentos/{item.Id}", Snapshot(item));
    }

    [HttpPut("departamentos/{id:long}")]
    public async Task<IActionResult> EditarDepartamento(long id, DepartamentoRequest entrada) { var item = await db.Departamentos.FindAsync(id); if (item is null) return NotFound(new ApiErrorResponse("Departamento no encontrado.")); if (string.IsNullOrWhiteSpace(entrada.Nombre)) return BadRequest(new ApiErrorResponse("El nombre del departamento es obligatorio.")); if (await db.Departamentos.AnyAsync(x => x.Id != id && (x.Nombre == entrada.Nombre || (entrada.Codigo != null && x.Codigo == entrada.Codigo)))) return Conflict(new ApiErrorResponse("El nombre o código del departamento ya existe.")); var before = Snapshot(item); item.Codigo = entrada.Codigo; item.Nombre = entrada.Nombre; item.Descripcion = entrada.Descripcion; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "DEPARTAMENTO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(Snapshot(item)); }

    [HttpPost("empleados")]
    public async Task<IActionResult> CrearEmpleado(EmpleadoRequest request) { if (string.IsNullOrWhiteSpace(request.CodigoEmpleado) || string.IsNullOrWhiteSpace(request.NombreCompleto) || string.IsNullOrWhiteSpace(request.Cedula)) return BadRequest(new ApiErrorResponse("Código, nombre y cédula son obligatorios.")); if (!CorreoValido(request.Correo)) return BadRequest(new ApiErrorResponse("El correo del empleado no es válido.")); if (!await db.Departamentos.AnyAsync(x => x.Id == request.DepartamentoId && x.Activo)) return BadRequest(new ApiErrorResponse("El departamento no existe o está inactivo.")); if (await db.Empleados.AnyAsync(x => x.CodigoEmpleado == request.CodigoEmpleado || x.Cedula == request.Cedula)) return Conflict(new ApiErrorResponse("El código o cédula del empleado ya existe.")); var item = new Empleado { CodigoEmpleado = request.CodigoEmpleado, NombreCompleto = request.NombreCompleto, Cedula = request.Cedula, DepartamentoId = request.DepartamentoId, Cargo = request.Cargo, Correo = request.Correo, TelefonoMovil = request.TelefonoMovil, Activo = request.Activo }; db.Empleados.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "EMPLEADO", item.Id.ToString(), "EXITO", datosNuevos: Snapshot(item)); await tx.CommitAsync(); return Created($"api/gestion/empleados/{item.Id}", Snapshot(item)); }

    [HttpPut("empleados/{id:long}")]
    public async Task<IActionResult> EditarEmpleado(long id, EmpleadoRequest entrada) { var item = await db.Empleados.FindAsync(id); if (item is null) return NotFound(new ApiErrorResponse("Empleado no encontrado.")); if (string.IsNullOrWhiteSpace(entrada.CodigoEmpleado) || string.IsNullOrWhiteSpace(entrada.NombreCompleto) || string.IsNullOrWhiteSpace(entrada.Cedula)) return BadRequest(new ApiErrorResponse("Código, nombre y cédula son obligatorios.")); if (!CorreoValido(entrada.Correo)) return BadRequest(new ApiErrorResponse("El correo del empleado no es válido.")); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest(new ApiErrorResponse("Departamento inválido.")); if (await db.Empleados.AnyAsync(x => x.Id != id && (x.CodigoEmpleado == entrada.CodigoEmpleado || x.Cedula == entrada.Cedula))) return Conflict(new ApiErrorResponse("El código o cédula del empleado ya existe.")); var before = Snapshot(item); item.CodigoEmpleado = entrada.CodigoEmpleado; item.NombreCompleto = entrada.NombreCompleto; item.Cedula = entrada.Cedula; item.DepartamentoId = entrada.DepartamentoId; item.Cargo = entrada.Cargo; item.Correo = entrada.Correo; item.TelefonoMovil = entrada.TelefonoMovil; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "EMPLEADO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(Snapshot(item)); }

    [HttpPost("vehiculos")]
    public async Task<IActionResult> CrearVehiculo(VehiculoRequest request) { var validation = ValidarVehiculo(request); if (validation is not null) return BadRequest(validation); if (!await db.Departamentos.AnyAsync(x => x.Id == request.DepartamentoId && x.Activo)) return BadRequest(new ApiErrorResponse("El departamento no existe o está inactivo.")); if (await db.Vehiculos.AnyAsync(x => x.Placa == request.Placa || x.Ficha == request.Ficha)) return Conflict(new ApiErrorResponse("La placa o ficha del vehículo ya existe.")); var item = new Vehiculo { Placa = request.Placa, Ficha = request.Ficha, Marca = request.Marca, Modelo = request.Modelo, Anio = request.Anio, Tipo = request.Tipo, DepartamentoId = request.DepartamentoId, CapacidadTanqueGalones = request.CapacidadTanqueGalones, OdometroKm = request.OdometroKm, Activo = request.Activo }; db.Vehiculos.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "VEHICULO", item.Id.ToString(), "EXITO", datosNuevos: Snapshot(item)); await tx.CommitAsync(); return Created($"api/gestion/vehiculos/{item.Id}", Snapshot(item)); }

    [HttpPut("vehiculos/{id:long}")]
    public async Task<IActionResult> EditarVehiculo(long id, VehiculoRequest entrada) { var item = await db.Vehiculos.FindAsync(id); if (item is null) return NotFound(new ApiErrorResponse("Vehículo no encontrado.")); var validation = ValidarVehiculo(entrada); if (validation is not null) return BadRequest(validation); if (!await db.Departamentos.AnyAsync(x => x.Id == entrada.DepartamentoId && x.Activo)) return BadRequest(new ApiErrorResponse("Departamento inválido.")); if (await db.Vehiculos.AnyAsync(x => x.Id != id && (x.Placa == entrada.Placa || x.Ficha == entrada.Ficha))) return Conflict(new ApiErrorResponse("La placa o ficha del vehículo ya existe.")); var before = Snapshot(item); item.Placa = entrada.Placa; item.Ficha = entrada.Ficha; item.Marca = entrada.Marca; item.Modelo = entrada.Modelo; item.Anio = entrada.Anio; item.Tipo = entrada.Tipo; item.DepartamentoId = entrada.DepartamentoId; item.CapacidadTanqueGalones = entrada.CapacidadTanqueGalones; item.OdometroKm = entrada.OdometroKm; item.Activo = entrada.Activo; await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_UPDATED", "VEHICULO", item.Id.ToString(), "EXITO", before, Snapshot(item)); await tx.CommitAsync(); return Ok(Snapshot(item)); }

    [HttpPost("estaciones")]
    public async Task<IActionResult> CrearEstacion(EstacionRequest request) { if (string.IsNullOrWhiteSpace(request.Nombre)) return BadRequest(new ApiErrorResponse("El nombre de la estación es obligatorio.")); var item = new Estacion { Nombre = request.Nombre, Ubicacion = request.Ubicacion, Activo = request.Activo }; db.Estaciones.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "ESTACION", item.Id.ToString(), "EXITO", datosNuevos: Snapshot(item)); await tx.CommitAsync(); return Created($"api/gestion/estaciones/{item.Id}", Snapshot(item)); }

    [HttpPost("tanques")]
    public async Task<IActionResult> CrearTanque(TanqueRequest request) { if (!await db.Estaciones.AnyAsync(x => x.Id == request.EstacionId && x.Activo)) return BadRequest(new ApiErrorResponse("La estación no existe o está inactiva.")); if (!await db.TiposCombustible.AnyAsync(x => x.Id == request.TipoCombustibleId && x.Activo)) return BadRequest(new ApiErrorResponse("Tipo de combustible inválido.")); if (request.CapacidadGalones <= 0 || request.NivelCriticoGalones < 0 || request.NivelCriticoGalones > request.CapacidadGalones) return BadRequest(new ApiErrorResponse("El tanque nuevo debe iniciar vacío, con capacidad positiva y nivel crítico válido.")); var item = new Tanque { Codigo = request.Codigo, Nombre = request.Nombre, EstacionId = request.EstacionId, TipoCombustibleId = request.TipoCombustibleId, CapacidadGalones = request.CapacidadGalones, NivelCriticoGalones = request.NivelCriticoGalones, ExistenciaActualGalones = 0, Activo = request.Activo }; db.Tanques.Add(item); await using var tx = await db.Database.BeginTransactionAsync(); await db.SaveChangesAsync(); await auditoria.RegistrarAsync("CATALOG_CREATED", "TANQUE", item.Id.ToString(), "EXITO", datosNuevos: Snapshot(item)); await tx.CommitAsync(); return Created($"api/gestion/tanques/{item.Id}", Snapshot(item)); }

    [HttpDelete("{tipo}/{id:long}")]
    public async Task<IActionResult> Desactivar(string tipo, long id)
    {
        object? before = null;
        object? after = null;
        string? entity = null;
        switch (tipo.ToLowerInvariant())
        {
            case "departamentos": { var x = await db.Departamentos.FindAsync(id); if (x is null) return NotFound(new ApiErrorResponse("Departamento no encontrado.")); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "DEPARTAMENTO"; break; }
            case "empleados": { var x = await db.Empleados.FindAsync(id); if (x is null) return NotFound(new ApiErrorResponse("Empleado no encontrado.")); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "EMPLEADO"; break; }
            case "vehiculos": { var x = await db.Vehiculos.FindAsync(id); if (x is null) return NotFound(new ApiErrorResponse("Vehículo no encontrado.")); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "VEHICULO"; break; }
            case "estaciones": { var x = await db.Estaciones.FindAsync(id); if (x is null) return NotFound(new ApiErrorResponse("Estación no encontrada.")); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "ESTACION"; break; }
            case "tanques": { var x = await db.Tanques.FindAsync(id); if (x is null) return NotFound(new ApiErrorResponse("Tanque no encontrado.")); before = Snapshot(x); x.Activo = false; after = Snapshot(x); entity = "TANQUE"; break; }
            default: return BadRequest(new ApiErrorResponse("Tipo no válido."));
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
    private static string? ValidarVehiculo(VehiculoRequest item)
    {
        if (string.IsNullOrWhiteSpace(item.Placa) || string.IsNullOrWhiteSpace(item.Ficha) || string.IsNullOrWhiteSpace(item.Marca) || string.IsNullOrWhiteSpace(item.Modelo)) return "Placa, ficha, marca y modelo son obligatorios.";
        if (item.Anio is < 1900 or > 2200) return "El año del vehículo debe estar entre 1900 y 2200.";
        if (item.CapacidadTanqueGalones is <= 0) return "La capacidad del tanque debe ser mayor que cero.";
        if (item.OdometroKm < 0) return "El odómetro no puede ser negativo.";
        return null;
    }
}

public sealed record DepartamentoRequest(string? Codigo, string Nombre, string? Descripcion, bool Activo = true);
public sealed record EmpleadoRequest(string CodigoEmpleado, string NombreCompleto, string Cedula, long DepartamentoId, string? Cargo, string? Correo, string? TelefonoMovil, bool Activo = true);
public sealed record VehiculoRequest(string Placa, string Ficha, string Marca, string Modelo, short? Anio, string? Tipo, long DepartamentoId, decimal? CapacidadTanqueGalones, decimal OdometroKm, bool Activo = true);
public sealed record EstacionRequest(string Nombre, string? Ubicacion, bool Activo = true);
public sealed record TanqueRequest(string Codigo, string? Nombre, long EstacionId, long TipoCombustibleId, decimal CapacidadGalones, decimal NivelCriticoGalones, bool Activo = true);
