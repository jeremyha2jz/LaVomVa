using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public sealed record ReporteFiltros(string Tipo, DateOnly? Desde, DateOnly? Hasta, long? DepartamentoId,
    long? CombustibleId, long? EmpleadoId, long? VehiculoId, string? Estado, long? EstacionId,
    int Pagina = 1, int TamanoPagina = 50);

public sealed record ReporteGrupo(string Nombre, decimal Galones, int Cantidad);
public sealed class ReporteFila
{
    public long Id { get; init; }
    public DateTime FechaUtc { get; init; }
    public string Tipo { get; init; } = "";
    public string? Ticket { get; init; }
    public string? Empleado { get; init; }
    public string? Vehiculo { get; init; }
    public string? Departamento { get; init; }
    public string? Combustible { get; init; }
    public string? Estacion { get; init; }
    public string? Tanque { get; init; }
    public string? Estado { get; init; }
    public decimal Galones { get; init; }
    public string? Referencia { get; init; }
    public string? Usuario { get; init; }
}

public sealed record ReporteTotales(long Registros, decimal Galones, int Despachos, int Tickets,
    int Solicitudes, decimal InventarioActualGalones);
public sealed record ReporteResultado(string Tipo, string RangoUtc, DateTime GeneradoEnUtc,
    ReporteFiltros Filtros, int Pagina, int TamanoPagina, long TotalRegistros,
    ReporteTotales Totales, IReadOnlyList<ReporteGrupo> PorDepartamento,
    IReadOnlyList<ReporteGrupo> PorCombustible, IReadOnlyList<ReporteGrupo> PorVehiculo,
    IReadOnlyList<ReporteGrupo> PorEmpleado, IReadOnlyList<ReporteGrupo> PorEstado,
    IReadOnlyList<ReporteFila> Items);

public sealed class ReporteFiltroException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Builds one filtered, server-side report model consumed by JSON and every file format.</summary>
public sealed class ReportesService(TicketsCombustibleDbContext db, TicketLifecycleService lifecycle, TimeProvider clock)
{
    public const int MaxPageSize = 200;
    public const int MaxExportRows = 10000;

    public async Task<ReporteResultado> ConsultarAsync(ReporteFiltros filtros, bool exportacion = false, CancellationToken ct = default)
    {
        var tipo = filtros.Tipo.Trim().ToLowerInvariant();
        if (tipo is not ("consumo" or "tickets" or "despachos" or "movimientos"))
            throw new ReporteFiltroException("El tipo debe ser consumo, tickets, despachos o movimientos.");
        if (filtros.Desde.HasValue && filtros.Hasta.HasValue && filtros.Desde > filtros.Hasta)
            throw new ReporteFiltroException("La fecha inicial no puede ser posterior a la fecha final.");
        if (filtros.Pagina < 1) throw new ReporteFiltroException("La página debe ser mayor que cero.");
        var maxSize = exportacion ? MaxExportRows : MaxPageSize;
        if (filtros.TamanoPagina < 1 || filtros.TamanoPagina > maxSize)
            throw new ReporteFiltroException($"El tamaño de página debe estar entre 1 y {maxSize}.");
        await ValidarExistenciaAsync(filtros, ct);

        EstadoTicket? estado = null;
        if (!string.IsNullOrWhiteSpace(filtros.Estado))
        {
            if (!Enum.TryParse<EstadoTicket>(filtros.Estado, true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ReporteFiltroException("El estado de ticket no es válido.");
            if (tipo == "movimientos") throw new ReporteFiltroException("El filtro de estado solo aplica a tickets, despachos y consumo.");
            estado = parsed;
        }

        var start = filtros.Desde?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var endExclusive = filtros.Hasta?.AddDays(1).ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue;
        var reportNow = lifecycle.UtcNow;
        var tickets = db.Tickets.AsNoTracking().AsQueryable();
        if (filtros.DepartamentoId is long departmentId) tickets = tickets.Where(x => x.DepartamentoId == departmentId);
        if (filtros.CombustibleId is long fuelId) tickets = tickets.Where(x => x.TipoCombustibleId == fuelId);
        if (filtros.EmpleadoId is long employeeId) tickets = tickets.Where(x => x.EmpleadoId == employeeId);
        if (filtros.VehiculoId is long vehicleId) tickets = tickets.Where(x => x.VehiculoId == vehicleId);
        if (estado.HasValue) tickets = lifecycle.FiltrarPorEstado(tickets, estado.Value, reportNow);

        IQueryable<ReporteFila> rows = tipo switch
        {
            "tickets" => TicketRows(tickets, filtros, start, endExclusive, reportNow),
            "movimientos" => MovementRows(tickets, filtros, start, endExclusive),
            _ => DispatchRows(tickets, filtros, start, endExclusive, reportNow)
        };

        var total = await rows.LongCountAsync(ct);
        if (exportacion && total > MaxExportRows)
            throw new ReporteFiltroException($"El reporte excede el máximo de {MaxExportRows} filas por archivo; aplique filtros.", 400);
        var offset = exportacion ? 0L : (long)(filtros.Pagina - 1) * filtros.TamanoPagina;
        var limit = exportacion ? MaxExportRows : filtros.TamanoPagina;
        var ordered = rows.OrderByDescending(x => x.FechaUtc).ThenByDescending(x => x.Id);
        var items = await ordered.Skip((int)Math.Min(offset, int.MaxValue)).Take(limit).ToListAsync(ct);

        var totalsQuery = rows.GroupBy(_ => 1).Select(g => new { Galones = g.Sum(x => x.Galones), Count = g.Count() });
        var sums = await totalsQuery.SingleOrDefaultAsync(ct);
        // Movement rows have no department/employee/vehicle values for inventory-only
        // operations. Aggregate their meaningful dimensions directly over SQL entities;
        // grouping a projected DTO makes EF attempt to group by the whole DTO.
        var byDepartment = tipo == "movimientos" ? [] : await GroupAsync(rows, x => x.Departamento, ct);
        var byFuel = tipo == "movimientos" ? await MovementGroupsByFuelAsync(filtros, start, endExclusive, ct) : await GroupAsync(rows, x => x.Combustible, ct);
        var byVehicle = tipo == "movimientos" ? [] : await GroupAsync(rows, x => x.Vehiculo, ct);
        var byEmployee = tipo == "movimientos" ? [] : await GroupAsync(rows, x => x.Empleado, ct);
        var byStatus = tipo == "movimientos" ? await MovementGroupsByTypeAsync(filtros, start, endExclusive, ct) : await GroupAsync(rows, x => x.Estado ?? "Sin estado", ct);

        var requestQuery = db.Solicitudes.AsNoTracking().AsQueryable()
            .Where(x => x.FechaSolicitud >= start && x.FechaSolicitud < endExclusive);
        if (filtros.DepartamentoId is long rd) requestQuery = requestQuery.Where(x => x.DepartamentoId == rd);
        if (filtros.CombustibleId is long rf) requestQuery = requestQuery.Where(x => x.TipoCombustibleId == rf);
        if (filtros.EmpleadoId is long re) requestQuery = requestQuery.Where(x => x.EmpleadoId == re);
        if (filtros.VehiculoId is long rv) requestQuery = requestQuery.Where(x => x.VehiculoId == rv);
        var requestCount = await requestQuery.CountAsync(ct);

        var inventoryQuery = db.Tanques.AsNoTracking().Where(x => x.Activo);
        if (filtros.EstacionId is long stationId) inventoryQuery = inventoryQuery.Where(x => x.EstacionId == stationId);
        if (filtros.CombustibleId is long invFuel) inventoryQuery = inventoryQuery.Where(x => x.TipoCombustibleId == invFuel);
        var inventory = await inventoryQuery.SumAsync(x => (decimal?)x.ExistenciaActualGalones, ct) ?? 0m;

        var matchedTickets = tipo == "movimientos" ? 0L : await rows.Where(x => x.Ticket != null).Select(x => x.Ticket).Distinct().LongCountAsync(ct);
        var matchedDespachos = tipo is "consumo" or "despachos" ? checked((int)Math.Min(total, int.MaxValue))
            : tipo == "movimientos" ? byStatus.Where(x => x.Nombre == "SALIDA").Sum(x => x.Cantidad)
            : await db.Despachos.AsNoTracking().Join(tickets, d => d.TicketId, t => t.Id, (d, _) => d)
                .Where(d => d.FechaHora >= start && d.FechaHora < endExclusive)
                .Where(d => filtros.EstacionId == null || d.EstacionId == filtros.EstacionId)
                .CountAsync(ct);
        var matchedTicketCount = tipo == "tickets" ? checked((int)Math.Min(total, int.MaxValue)) : checked((int)Math.Min(matchedTickets, int.MaxValue));
        var reportFilters = filtros with { Tipo = tipo };
        return new ReporteResultado(tipo, $"{filtros.Desde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "inicio"}–{filtros.Hasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "hoy"} (UTC, ambos inclusive)",
            clock.GetUtcNow().UtcDateTime, reportFilters, exportacion ? 1 : filtros.Pagina, filtros.TamanoPagina, total,
            new ReporteTotales(total, sums?.Galones ?? 0m, matchedDespachos, matchedTicketCount, requestCount, inventory),
            byDepartment, byFuel, byVehicle, byEmployee, byStatus, items);
    }

    private IQueryable<ReporteFila> TicketRows(IQueryable<Ticket> tickets, ReporteFiltros f, DateTime start, DateTime end, DateTime reportNow) =>
        from t in tickets.Where(x => x.FechaCreacion >= start && x.FechaCreacion < end)
        join e in db.Empleados.AsNoTracking() on t.EmpleadoId equals e.Id
        join v in db.Vehiculos.AsNoTracking() on t.VehiculoId equals v.Id
        join dep in db.Departamentos.AsNoTracking() on t.DepartamentoId equals dep.Id
        join fuel in db.TiposCombustible.AsNoTracking() on t.TipoCombustibleId equals fuel.Id
        join d0 in db.Despachos.AsNoTracking() on t.Id equals d0.TicketId into dispatches
        from d in dispatches.DefaultIfEmpty()
        join st0 in db.Estaciones.AsNoTracking() on (long?)d.EstacionId equals (long?)st0.Id into stations
        from station in stations.DefaultIfEmpty()
        where f.EstacionId == null || (d != null && d.EstacionId == f.EstacionId)
        select new ReporteFila { Id = t.SolicitudId, FechaUtc = t.FechaCreacion, Tipo = "TICKET", Ticket = t.NumeroSecuencial,
            Empleado = e.NombreCompleto, Vehiculo = v.Placa, Departamento = dep.Nombre, Combustible = fuel.Nombre,
            Estacion = station == null ? null : station.Nombre,
            Estado = t.Estado == EstadoTicket.CONSUMIDO ? "CONSUMIDO" : t.Estado == EstadoTicket.ANULADO ? "ANULADO" :
                t.FechaVencimiento <= reportNow ? "VENCIDO" :
                t.FechaVencimiento <= reportNow.Add(TicketLifecycleService.ProximoAVencerThreshold) ? "PROXIMO_A_VENCER" :
                t.Estado == EstadoTicket.ENVIADO ? "ENVIADO" : t.Estado == EstadoTicket.PENDIENTE ? "PENDIENTE" : "CREADO",
            Galones = t.CantidadAutorizadaGalones,
            Referencia = t.AnuladoEn == null ? null : "Anulado: " + t.MotivoAnulacion, Usuario = null };

    private IQueryable<ReporteFila> DispatchRows(IQueryable<Ticket> tickets, ReporteFiltros f, DateTime start, DateTime end, DateTime reportNow) =>
        from d in db.Despachos.AsNoTracking().Where(x => x.FechaHora >= start && x.FechaHora < end && (f.EstacionId == null || x.EstacionId == f.EstacionId))
        join t in tickets on d.TicketId equals t.Id
        join e in db.Empleados.AsNoTracking() on t.EmpleadoId equals e.Id
        join v in db.Vehiculos.AsNoTracking() on t.VehiculoId equals v.Id
        join dep in db.Departamentos.AsNoTracking() on t.DepartamentoId equals dep.Id
        join fuel in db.TiposCombustible.AsNoTracking() on t.TipoCombustibleId equals fuel.Id
        join station in db.Estaciones.AsNoTracking() on d.EstacionId equals station.Id
        join tank in db.Tanques.AsNoTracking() on d.TanqueId equals tank.Id
        join user in db.Usuarios.AsNoTracking() on d.OperadorId equals user.Id
        select new ReporteFila { Id = d.Id, FechaUtc = d.FechaHora, Tipo = "DESPACHO", Ticket = t.NumeroSecuencial,
            Empleado = e.NombreCompleto, Vehiculo = v.Placa, Departamento = dep.Nombre, Combustible = fuel.Nombre,
            Estacion = station.Nombre, Tanque = tank.Codigo,
            Estado = t.Estado == EstadoTicket.CONSUMIDO ? "CONSUMIDO" : t.Estado == EstadoTicket.ANULADO ? "ANULADO" :
                t.FechaVencimiento <= reportNow ? "VENCIDO" :
                t.FechaVencimiento <= reportNow.Add(TicketLifecycleService.ProximoAVencerThreshold) ? "PROXIMO_A_VENCER" :
                t.Estado == EstadoTicket.ENVIADO ? "ENVIADO" : t.Estado == EstadoTicket.PENDIENTE ? "PENDIENTE" : "CREADO",
            Galones = d.GalonesServidos,
            Referencia = d.Observaciones, Usuario = user.NombreCompleto };

    private IQueryable<ReporteFila> MovementRows(IQueryable<Ticket> tickets, ReporteFiltros f, DateTime start, DateTime end) =>
        from m in db.MovimientosInventario.AsNoTracking().Where(x => x.FechaHora >= start && x.FechaHora < end)
        join tank in db.Tanques.AsNoTracking() on m.TanqueId equals tank.Id
        join station in db.Estaciones.AsNoTracking() on tank.EstacionId equals station.Id
        join fuel in db.TiposCombustible.AsNoTracking() on tank.TipoCombustibleId equals fuel.Id
        join user0 in db.Usuarios.AsNoTracking() on m.UsuarioId equals (long?)user0.Id into users
        from user in users.DefaultIfEmpty()
        where (f.EstacionId == null || tank.EstacionId == f.EstacionId) && (f.CombustibleId == null || tank.TipoCombustibleId == f.CombustibleId)
            && (f.DepartamentoId == null || db.Despachos.AsNoTracking().Where(d => m.ReferenciaTipo == "DESPACHO" && m.ReferenciaId == d.Id.ToString())
                .Join(tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.DepartamentoId == f.DepartamentoId))
            && (f.EmpleadoId == null || db.Despachos.AsNoTracking().Where(d => m.ReferenciaTipo == "DESPACHO" && m.ReferenciaId == d.Id.ToString())
                .Join(tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.EmpleadoId == f.EmpleadoId))
            && (f.VehiculoId == null || db.Despachos.AsNoTracking().Where(d => m.ReferenciaTipo == "DESPACHO" && m.ReferenciaId == d.Id.ToString())
                .Join(tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.VehiculoId == f.VehiculoId))
        select new ReporteFila { Id = m.Id, FechaUtc = m.FechaHora, Tipo = m.TipoMovimiento, Combustible = fuel.Nombre,
            Estacion = station.Nombre, Tanque = tank.Codigo, Galones = m.CantidadGalones, Referencia = m.ReferenciaTipo + ":" + m.ReferenciaId,
            Usuario = user == null ? "Sistema" : user.NombreCompleto };

    private async Task<List<ReporteGrupo>> MovementGroupsByFuelAsync(ReporteFiltros f, DateTime start, DateTime end, CancellationToken ct)
    {
        var grouped = await db.MovimientosInventario.AsNoTracking()
            .Where(m => m.FechaHora >= start && m.FechaHora < end)
            .Join(db.Tanques.AsNoTracking(), m => m.TanqueId, t => t.Id, (m, t) => new { m, t })
            .Join(db.TiposCombustible.AsNoTracking(), x => x.t.TipoCombustibleId, fuel => fuel.Id, (x, fuel) => new { x.m, x.t, fuel })
            .Where(x => (f.EstacionId == null || x.t.EstacionId == f.EstacionId) && (f.CombustibleId == null || x.t.TipoCombustibleId == f.CombustibleId)
                && (f.DepartamentoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.DepartamentoId == f.DepartamentoId))
                && (f.EmpleadoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.EmpleadoId == f.EmpleadoId))
                && (f.VehiculoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.VehiculoId == f.VehiculoId)))
            .GroupBy(x => x.fuel.Nombre)
            .Select(g => new { Nombre = g.Key, Galones = g.Sum(x => x.m.CantidadGalones), Cantidad = g.Count() })
            .OrderByDescending(x => x.Galones).ThenBy(x => x.Nombre).ToListAsync(ct);
        return grouped.Select(x => new ReporteGrupo(x.Nombre, x.Galones, x.Cantidad)).ToList();
    }

    private async Task<List<ReporteGrupo>> MovementGroupsByTypeAsync(ReporteFiltros f, DateTime start, DateTime end, CancellationToken ct)
    {
        var grouped = await db.MovimientosInventario.AsNoTracking()
            .Where(m => m.FechaHora >= start && m.FechaHora < end)
            .Join(db.Tanques.AsNoTracking(), m => m.TanqueId, t => t.Id, (m, t) => new { m, t })
            .Where(x => (f.EstacionId == null || x.t.EstacionId == f.EstacionId) && (f.CombustibleId == null || x.t.TipoCombustibleId == f.CombustibleId)
                && (f.DepartamentoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.DepartamentoId == f.DepartamentoId))
                && (f.EmpleadoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.EmpleadoId == f.EmpleadoId))
                && (f.VehiculoId == null || db.Despachos.Where(d => x.m.ReferenciaTipo == "DESPACHO" && x.m.ReferenciaId == d.Id.ToString())
                    .Join(db.Tickets, d => d.TicketId, t => t.Id, (_, t) => t).Any(t => t.VehiculoId == f.VehiculoId)))
            .GroupBy(x => x.m.TipoMovimiento)
            .Select(g => new { Nombre = g.Key, Galones = g.Sum(x => x.m.CantidadGalones), Cantidad = g.Count() })
            .OrderByDescending(x => x.Galones).ThenBy(x => x.Nombre).ToListAsync(ct);
        return grouped.Select(x => new ReporteGrupo(x.Nombre, x.Galones, x.Cantidad)).ToList();
    }

    private static async Task<List<ReporteGrupo>> GroupAsync(IQueryable<ReporteFila> rows, System.Linq.Expressions.Expression<Func<ReporteFila, string?>> key, CancellationToken ct)
    {
        var grouped = await rows.GroupBy(key)
            .Select(g => new { Nombre = g.Key, Galones = g.Sum(x => x.Galones), Cantidad = g.Count() })
            .OrderByDescending(x => x.Galones).ThenBy(x => x.Nombre).ToListAsync(ct);
        return grouped.Select(x => new ReporteGrupo(x.Nombre ?? "Sin dato", x.Galones, x.Cantidad)).ToList();
    }

    private async Task ValidarExistenciaAsync(ReporteFiltros f, CancellationToken ct)
    {
        if (f.DepartamentoId is long d && !await db.Departamentos.AnyAsync(x => x.Id == d, ct)) throw new ReporteFiltroException("El departamento indicado no existe.", 404);
        if (f.CombustibleId is long c && !await db.TiposCombustible.AnyAsync(x => x.Id == c, ct)) throw new ReporteFiltroException("El combustible indicado no existe.", 404);
        if (f.EmpleadoId is long e && !await db.Empleados.AnyAsync(x => x.Id == e, ct)) throw new ReporteFiltroException("El empleado indicado no existe.", 404);
        if (f.VehiculoId is long v && !await db.Vehiculos.AnyAsync(x => x.Id == v, ct)) throw new ReporteFiltroException("El vehículo indicado no existe.", 404);
        if (f.EstacionId is long s && !await db.Estaciones.AnyAsync(x => x.Id == s, ct)) throw new ReporteFiltroException("La estación indicada no existe.", 404);
    }
}
