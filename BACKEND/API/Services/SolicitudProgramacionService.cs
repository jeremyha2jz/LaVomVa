using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public sealed class ProgramacionSolicitudInvalida(string message) : Exception(message);
public sealed class ProgramacionSolicitudNoEncontrada(string message) : Exception(message);
public sealed class ProgramacionSolicitudConflicto(string message) : Exception(message);

public interface ISolicitudProgramacionProcessor
{
    Task<int> ProcessDueAsync(CancellationToken cancellationToken);
}

/// <summary>Stores schedules on the existing request model and processes due runs transactionally.</summary>
public sealed class SolicitudProgramacionService(
    TicketsCombustibleDbContext db,
    IAuditoriaService auditoria,
    TimeProvider timeProvider) : ISolicitudProgramacionProcessor
{
    private const int BatchLimit = 100;
    private const decimal MaxRequestGallons = 99_999_999.99m;

    public async Task<IReadOnlyList<ProgramacionSolicitudResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await db.ProgramacionesSolicitud.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync(cancellationToken))
        .Select(ToResponse).ToArray();

    public async Task<ProgramacionSolicitudResponse> GetAsync(long id, CancellationToken cancellationToken) =>
        ToResponse(await db.ProgramacionesSolicitud.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ProgramacionSolicitudNoEncontrada("Programación no encontrada."));

    public async Task<IReadOnlyList<EjecucionProgramadaResponse>> HistoryAsync(long id, CancellationToken cancellationToken)
    {
        if (!await db.ProgramacionesSolicitud.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            throw new ProgramacionSolicitudNoEncontrada("Programación no encontrada.");
        var executions = await db.EjecucionesProgramadas.AsNoTracking().Where(x => x.ProgramacionId == id)
            .OrderByDescending(x => x.FechaProgramada).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);
        var requestIds = executions.Where(x => x.SolicitudGeneradaId.HasValue).Select(x => x.SolicitudGeneradaId!.Value).ToArray();
        var states = await db.Solicitudes.AsNoTracking().Where(x => requestIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Estado.ToString(), cancellationToken);
        return executions.Select(x => new EjecucionProgramadaResponse(x.Id, x.ProgramacionId,
            AsUtc(x.FechaProgramada), AsUtc(x.EjecutadaEn), x.Estado, x.SolicitudGeneradaId,
            x.SolicitudGeneradaId is { } requestId && states.TryGetValue(requestId, out var state) ? state : null,
            x.DetalleError)).ToArray();
    }

    public async Task<ProgramacionSolicitudResponse> CreateAsync(CrearProgramacionSolicitudRequest request, long actorId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeAndValidate(request);
        var referenceError = await ValidateReferencesAsync(normalized, cancellationToken);
        if (referenceError is not null) throw new ProgramacionSolicitudInvalida(referenceError);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = UtcNaive(timeProvider.GetUtcNow());
        var program = new ProgramacionSolicitud
        {
            TipoSolicitud = normalized.TipoSolicitud,
            EmpleadoId = normalized.EmpleadoId,
            VehiculoId = normalized.VehiculoId,
            DepartamentoId = normalized.DepartamentoId,
            TipoCombustibleId = normalized.TipoCombustibleId,
            CantidadSolicitadaGalones = normalized.CantidadSolicitadaGalones,
            FechaInicial = UtcNaive(normalized.FechaInicial),
            FechaFinal = normalized.FechaFinal is { } end ? UtcNaive(end) : null,
            Frecuencia = normalized.Frecuencia,
            ProximaEjecucion = UtcNaive(normalized.FechaInicial),
            Activa = true,
            UsuarioCreadorId = actorId,
            CreadoEn = now,
            ActualizadoEn = now
        };
        db.ProgramacionesSolicitud.Add(program);
        await db.SaveChangesAsync(cancellationToken);
        await auditoria.RegistrarAsync("PROGRAMACION_CREADA", "PROGRAMACION_SOLICITUD", program.Id.ToString(), "EXITO",
            datosNuevos: Snapshot(program), usuarioId: actorId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(program);
    }

    public async Task<ProgramacionSolicitudResponse> UpdateAsync(long id, CrearProgramacionSolicitudRequest request, long actorId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeAndValidate(request);
        var referenceError = await ValidateReferencesAsync(normalized, cancellationToken);
        if (referenceError is not null) throw new ProgramacionSolicitudInvalida(referenceError);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var program = await LoadForUpdateAsync(id, cancellationToken);
        var before = Snapshot(program);
        program.TipoSolicitud = normalized.TipoSolicitud;
        program.EmpleadoId = normalized.EmpleadoId;
        program.VehiculoId = normalized.VehiculoId;
        program.DepartamentoId = normalized.DepartamentoId;
        program.TipoCombustibleId = normalized.TipoCombustibleId;
        program.CantidadSolicitadaGalones = normalized.CantidadSolicitadaGalones;
        program.FechaInicial = UtcNaive(normalized.FechaInicial);
        program.FechaFinal = normalized.FechaFinal is { } end ? UtcNaive(end) : null;
        program.Frecuencia = normalized.Frecuencia;
        program.ActualizadoEn = UtcNaive(timeProvider.GetUtcNow());
        if (program.Activa)
            program.ProximaEjecucion = await NextNotPreviouslyExecutedAsync(program, UtcNaive(timeProvider.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await auditoria.RegistrarAsync("PROGRAMACION_MODIFICADA", "PROGRAMACION_SOLICITUD", id.ToString(), "EXITO",
            before, Snapshot(program), usuarioId: actorId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(program);
    }

    public async Task<ProgramacionSolicitudResponse> SetActiveAsync(long id, bool active, long actorId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var program = await LoadForUpdateAsync(id, cancellationToken);
        if (program.Activa == active)
            throw new ProgramacionSolicitudConflicto(active ? "La programación ya está activa." : "La programación ya está desactivada.");
        var before = Snapshot(program);
        if (active)
        {
            var referenceError = await ValidateReferencesAsync(program, cancellationToken);
            if (referenceError is not null) throw new ProgramacionSolicitudInvalida(referenceError);
            program.ProximaEjecucion = await NextNotPreviouslyExecutedAsync(program, UtcNaive(timeProvider.GetUtcNow()), cancellationToken);
            if (program.ProximaEjecucion is null) throw new ProgramacionSolicitudConflicto("No hay ejecuciones futuras dentro del rango de fechas.");
            program.Activa = true;
        }
        else
        {
            program.Activa = false;
            program.ProximaEjecucion = null;
        }
        program.ActualizadoEn = UtcNaive(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        var action = active ? "PROGRAMACION_ACTIVADA" : "PROGRAMACION_DESACTIVADA";
        await auditoria.RegistrarAsync(action, "PROGRAMACION_SOLICITUD", id.ToString(), "EXITO",
            before, Snapshot(program), usuarioId: actorId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResponse(program);
    }

    /// <summary>Runs at most one overdue occurrence per schedule. Missed periods are coalesced, never replayed as a backlog.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = UtcNaive(timeProvider.GetUtcNow());
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var due = await db.ProgramacionesSolicitud.FromSqlRaw("""
            SELECT * FROM programaciones_solicitud
            WHERE activa = TRUE AND proxima_ejecucion IS NOT NULL AND proxima_ejecucion <= @now
            ORDER BY proxima_ejecucion, id_programacion
            LIMIT @batchLimit
            FOR UPDATE SKIP LOCKED
            """,
            new NpgsqlParameter<DateTime>("now", NpgsqlDbType.Timestamp) { TypedValue = now },
            new NpgsqlParameter<int>("batchLimit", NpgsqlDbType.Integer) { TypedValue = BatchLimit })
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (var program in due)
        {
            var scheduledFor = program.ProximaEjecucion!.Value;
            var referenceError = await ValidateReferencesAsync(program, cancellationToken);
            if (referenceError is not null)
            {
                var error = referenceError.Length > 500 ? referenceError[..500] : referenceError;
                db.EjecucionesProgramadas.Add(new EjecucionProgramada
                {
                    ProgramacionId = program.Id, FechaProgramada = scheduledFor, EjecutadaEn = now,
                    Estado = "FALLIDA", DetalleError = error
                });
                // Pause invalid references; the administrator can fix the source catalogs and reactivate.
                program.Activa = false;
                program.ProximaEjecucion = null;
                program.UltimaEjecucion = now;
                program.ActualizadoEn = now;
                await db.SaveChangesAsync(cancellationToken);
                await auditoria.RegistrarAsync("EJECUCION_PROGRAMADA_FALLIDA", "PROGRAMACION_SOLICITUD", program.Id.ToString(), "FALLO",
                    datosNuevos: new { programacionId = program.Id, tipoSolicitud = program.TipoSolicitud, fechaProgramada = AsUtc(scheduledFor), fechaReal = AsUtc(now), error },
                    usuarioId: null, cancellationToken: cancellationToken);
                continue;
            }

            var generated = new SolicitudCombustible
            {
                EmpleadoId = program.EmpleadoId,
                VehiculoId = program.VehiculoId,
                DepartamentoId = program.DepartamentoId,
                TipoCombustibleId = program.TipoCombustibleId,
                CantidadSolicitadaGalones = program.CantidadSolicitadaGalones,
                TipoSolicitud = program.TipoSolicitud,
                Frecuencia = program.Frecuencia,
                Motivo = $"Generada por programación #{program.Id}; pendiente de aprobación.",
                Estado = EstadoSolicitud.PENDIENTE,
                UsuarioCreadorId = null
            };
            db.Solicitudes.Add(generated);
            await db.SaveChangesAsync(cancellationToken);
            db.EjecucionesProgramadas.Add(new EjecucionProgramada
            {
                ProgramacionId = program.Id,
                FechaProgramada = scheduledFor,
                EjecutadaEn = now,
                Estado = "GENERADA",
                SolicitudGeneradaId = generated.Id
            });
            program.UltimaEjecucion = now;
            program.ActualizadoEn = now;
            if (program.TipoSolicitud == "AUTOMATICA")
            {
                program.Activa = false;
                program.ProximaEjecucion = null;
            }
            else
            {
                var next = NextOccurrenceAtOrAfter(program.FechaInicial, program.Frecuencia!, now.AddTicks(1));
                if (program.FechaFinal is { } end && next > end)
                {
                    program.Activa = false;
                    program.ProximaEjecucion = null;
                }
                else program.ProximaEjecucion = next;
            }
            await db.SaveChangesAsync(cancellationToken);
            var action = program.TipoSolicitud == "AUTOMATICA" ? "SOLICITUD_AUTOMATICA_GENERADA" : "SOLICITUD_RECURRENTE_GENERADA";
            await auditoria.RegistrarAsync(action, "SOLICITUD", generated.Id.ToString(), "EXITO",
                datosNuevos: new { programacionId = program.Id, solicitudId = generated.Id, fechaProgramada = AsUtc(scheduledFor), fechaReal = AsUtc(now), estadoSolicitud = generated.Estado.ToString() },
                usuarioId: null, cancellationToken: cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return due.Count;
    }

    public static DateTime NextOccurrenceAtOrAfter(DateTime startUtc, string frequency, DateTime targetUtc)
    {
        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var target = DateTime.SpecifyKind(targetUtc, DateTimeKind.Utc);
        if (target <= start) return DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        if (frequency is "DIARIA" or "SEMANAL")
        {
            var step = TimeSpan.FromDays(frequency == "DIARIA" ? 1 : 7).Ticks;
            var elapsed = (target.Ticks - start.Ticks);
            var count = (elapsed + step - 1) / step;
            return DateTime.SpecifyKind(start.AddTicks(count * step), DateTimeKind.Unspecified);
        }
        if (frequency != "MENSUAL") throw new ArgumentOutOfRangeException(nameof(frequency));
        var monthOffset = (target.Year - start.Year) * 12 + target.Month - start.Month;
        var candidate = start.AddMonths(monthOffset);
        if (candidate < target) candidate = start.AddMonths(monthOffset + 1);
        return DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
    }

    private async Task<DateTime?> NextNotPreviouslyExecutedAsync(ProgramacionSolicitud program, DateTime now, CancellationToken cancellationToken)
    {
        if (program.TipoSolicitud == "AUTOMATICA")
        {
            if (await db.EjecucionesProgramadas.AnyAsync(x => x.ProgramacionId == program.Id && x.Estado == "GENERADA", cancellationToken))
                throw new ProgramacionSolicitudConflicto("Una programación automática que generó una solicitud no se puede reactivar.");
            var lastAttempt = await db.EjecucionesProgramadas.Where(x => x.ProgramacionId == program.Id)
                .Select(x => (DateTime?)x.FechaProgramada).MaxAsync(cancellationToken);
            var retryAfterFailure = lastAttempt is { } attempted && attempted >= now ? attempted.AddSeconds(1) : now;
            var nextOnce = retryAfterFailure > program.FechaInicial ? retryAfterFailure : program.FechaInicial;
            return program.FechaFinal is { } end && nextOnce > end ? null : nextOnce;
        }
        var lastPlanned = await db.EjecucionesProgramadas.Where(x => x.ProgramacionId == program.Id)
            .OrderByDescending(x => x.FechaProgramada).Select(x => (DateTime?)x.FechaProgramada).FirstOrDefaultAsync(cancellationToken);
        var target = lastPlanned is { } last && last >= now ? last.AddTicks(1) : now;
        var next = NextOccurrenceAtOrAfter(program.FechaInicial, program.Frecuencia!, target);
        return program.FechaFinal is { } finish && next > finish ? null : next;
    }

    private async Task<ProgramacionSolicitud> LoadForUpdateAsync(long id, CancellationToken cancellationToken) =>
        await db.ProgramacionesSolicitud.FromSqlInterpolated($"SELECT * FROM programaciones_solicitud WHERE id_programacion = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ProgramacionSolicitudNoEncontrada("Programación no encontrada.");

    private async Task<string?> ValidateReferencesAsync(CrearProgramacionSolicitudRequest request, CancellationToken cancellationToken)
    {
        var employee = await db.Empleados.AsNoTracking().Where(x => x.Id == request.EmpleadoId).Select(x => new { x.Activo, x.DepartamentoId }).SingleOrDefaultAsync(cancellationToken);
        var vehicle = await db.Vehiculos.AsNoTracking().Where(x => x.Id == request.VehiculoId).Select(x => new { x.Activo, x.DepartamentoId }).SingleOrDefaultAsync(cancellationToken);
        var department = await db.Departamentos.AsNoTracking().Where(x => x.Id == request.DepartamentoId).Select(x => x.Activo).SingleOrDefaultAsync(cancellationToken);
        var fuel = await db.TiposCombustible.AsNoTracking().Where(x => x.Id == request.TipoCombustibleId).Select(x => x.Activo).SingleOrDefaultAsync(cancellationToken);
        if (employee is null || !employee.Activo) return "El empleado de la programación no existe o está inactivo.";
        if (vehicle is null || !vehicle.Activo) return "El vehículo de la programación no existe o está inactivo.";
        if (!department) return "El departamento de la programación no existe o está inactivo.";
        if (!fuel) return "El combustible de la programación no existe o está inactivo.";
        if (employee.DepartamentoId != request.DepartamentoId || vehicle.DepartamentoId != request.DepartamentoId)
            return "El empleado, vehículo y departamento deben conservar la misma asociación.";
        return null;
    }

    private Task<string?> ValidateReferencesAsync(ProgramacionSolicitud program, CancellationToken cancellationToken) =>
        ValidateReferencesAsync(new CrearProgramacionSolicitudRequest(program.TipoSolicitud, program.EmpleadoId,
            program.VehiculoId, program.DepartamentoId, program.TipoCombustibleId, program.CantidadSolicitadaGalones,
            AsUtc(program.FechaInicial), program.FechaFinal is { } end ? AsUtc(end) : null, program.Frecuencia), cancellationToken);

    private static CrearProgramacionSolicitudRequest NormalizeAndValidate(CrearProgramacionSolicitudRequest request)
    {
        var type = request.TipoSolicitud.Trim().ToUpperInvariant();
        var frequency = string.IsNullOrWhiteSpace(request.Frecuencia) ? null : request.Frecuencia.Trim().ToUpperInvariant();
        var normalized = request with { TipoSolicitud = type, Frecuencia = frequency };
        if (type is not ("AUTOMATICA" or "RECURRENTE")) throw new ProgramacionSolicitudInvalida("El tipo debe ser AUTOMATICA o RECURRENTE.");
        if (type == "AUTOMATICA" && frequency is not null) throw new ProgramacionSolicitudInvalida("La programación automática es de una sola ejecución y no lleva frecuencia.");
        if (type == "RECURRENTE" && frequency is not ("DIARIA" or "SEMANAL" or "MENSUAL"))
            throw new ProgramacionSolicitudInvalida("La frecuencia recurrente debe ser DIARIA, SEMANAL o MENSUAL.");
        if (normalized.CantidadSolicitadaGalones <= 0 || normalized.CantidadSolicitadaGalones > MaxRequestGallons || decimal.Round(normalized.CantidadSolicitadaGalones, 2) != normalized.CantidadSolicitadaGalones)
            throw new ProgramacionSolicitudInvalida("La cantidad debe ser mayor que cero y admitir hasta dos decimales.");
        if (normalized.FechaFinal is { } finish && finish < normalized.FechaInicial)
            throw new ProgramacionSolicitudInvalida("La fecha final no puede ser anterior a la inicial.");
        if (normalized.FechaInicial.Year < 2000 || normalized.FechaInicial.Year > 9998 || normalized.FechaFinal?.Year > 9998)
            throw new ProgramacionSolicitudInvalida("La fecha está fuera del rango admitido.");
        return normalized;
    }

    private static ProgramacionSolicitudResponse ToResponse(ProgramacionSolicitud x) => new(x.Id, x.TipoSolicitud,
        x.EmpleadoId, x.VehiculoId, x.DepartamentoId, x.TipoCombustibleId, x.CantidadSolicitadaGalones,
        AsUtc(x.FechaInicial), x.FechaFinal is { } end ? AsUtc(end) : null, x.Frecuencia,
        x.ProximaEjecucion is { } next ? AsUtc(next) : null, x.UltimaEjecucion is { } last ? AsUtc(last) : null,
        x.Activa, x.UsuarioCreadorId, AsUtc(x.CreadoEn), AsUtc(x.ActualizadoEn));

    private static object Snapshot(ProgramacionSolicitud x) => new
    {
        x.Id, x.TipoSolicitud, x.EmpleadoId, x.VehiculoId, x.DepartamentoId, x.TipoCombustibleId,
        x.CantidadSolicitadaGalones, x.FechaInicial, x.FechaFinal, x.Frecuencia,
        x.ProximaEjecucion, x.UltimaEjecucion, x.Activa, x.UsuarioCreadorId
    };

    private static DateTime UtcNaive(DateTimeOffset value) => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
