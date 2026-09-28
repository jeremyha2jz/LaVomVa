using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

/// <summary>Persists user-scoped business notices in the caller's transaction.</summary>
public sealed class NotificacionService(TicketsCombustibleDbContext db, TimeProvider clock)
{
    public async Task CrearParaRolesAsync(
        IReadOnlyCollection<string> roles, string tipo, string titulo, string mensaje,
        string severidad, string referenciaTipo, string referenciaId, string clave,
        object metadata, CancellationToken cancellationToken = default)
    {
        var rolesArray = roles.ToArray();
        var json = JsonSerializer.Serialize(metadata);
        var now = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notificaciones
                (id_usuario,tipo,titulo,mensaje,canal,estado,fecha_creacion,referencia_tipo,referencia_id,severidad,clave_deduplicacion,metadata)
            SELECT DISTINCT u.id_usuario,{tipo},{titulo},{mensaje},'SISTEMA','PENDIENTE',timezone('UTC',{now}),{referenciaTipo},{referenciaId},{severidad},{clave},{json}::jsonb
              FROM usuarios u
              JOIN usuario_roles ur ON ur.id_usuario=u.id_usuario
              JOIN roles r ON r.id_role=ur.id_role AND r.activo=TRUE
             WHERE u.activo=TRUE AND r.nombre=ANY({rolesArray})
            ON CONFLICT (id_usuario,clave_deduplicacion)
              WHERE id_usuario IS NOT NULL AND clave_deduplicacion IS NOT NULL DO NOTHING
            """, cancellationToken);
    }

    /// <summary>Call within the stock-changing transaction after locking the tank row.</summary>
    public async Task SincronizarEpisodioInventarioAsync(long tankId, decimal stockAfter, CancellationToken cancellationToken = default)
    {
        var tank = await db.Tanques.FromSqlInterpolated($"SELECT * FROM tanques WHERE id_tanque={tankId} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);
        var below = stockAfter <= tank.NivelCriticoGalones;
        if (!below)
        {
            if (tank.NotificacionBajoActiva)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tanques SET notificacion_bajo_activa=FALSE WHERE id_tanque={tankId}", cancellationToken);
            return;
        }
        if (tank.NotificacionBajoActiva) return;

        var episode = tank.NumeroEpisodioBajo + 1;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tanques SET notificacion_bajo_activa=TRUE,numero_episodio_bajo={episode} WHERE id_tanque={tankId}", cancellationToken);
        await CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR"], "INVENTARIO_BAJO",
            "Inventario bajo", $"El tanque {tank.Codigo} tiene {stockAfter:0.##} galones y alcanzó su nivel crítico.",
            "CRITICA", "TANQUE", tankId.ToString(), $"tanque:{tankId}:inventario-bajo:{episode}",
            new { tanqueId = tankId, codigo = tank.Codigo, existenciaGalones = stockAfter, nivelCriticoGalones = tank.NivelCriticoGalones, episodio = episode }, cancellationToken);
    }

    public Task NotificarAjusteAsync(MovimientoInventario movimiento, CancellationToken cancellationToken = default) =>
        CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR"], "AJUSTE_INVENTARIO", "Ajuste de inventario",
            $"{movimiento.TipoMovimiento.Replace('_', ' ')} de {movimiento.CantidadGalones:0.##} galones registrado en el tanque {movimiento.TanqueId}.",
            movimiento.TipoMovimiento == "AJUSTE_POSITIVO" ? "INFO" : "AVISO", "MOVIMIENTO_INVENTARIO", movimiento.Id.ToString(),
            $"movimiento:{movimiento.Id}:AJUSTE_INVENTARIO",
            new { movimientoId = movimiento.Id, tanqueId = movimiento.TanqueId, tipo = movimiento.TipoMovimiento, cantidadGalones = movimiento.CantidadGalones }, cancellationToken);

}

public sealed class TicketNotificationProcessor(TicketsCombustibleDbContext db, TicketLifecycleService lifecycle, NotificacionService notifications)
{
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var now = lifecycle.UtcNow;
        var due = await db.Tickets.AsNoTracking()
            .Where(t => t.Estado != EstadoTicket.CONSUMIDO && t.Estado != EstadoTicket.ANULADO
                && t.FechaVencimiento <= now + TicketLifecycleService.ProximoAVencerThreshold)
            .OrderBy(t => t.FechaVencimiento)
            .Select(t => new Ticket { Id = t.Id, Estado = t.Estado, FechaVencimiento = t.FechaVencimiento, NumeroSecuencial = t.NumeroSecuencial })
            .ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var ticket in due)
        {
            var state = lifecycle.EstadoActual(ticket, now);
            if (state == EstadoTicket.PROXIMO_A_VENCER)
            {
                await notifications.CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR"], "TICKET_PROXIMO_A_VENCER",
                    "Ticket próximo a vencer", $"El ticket {ticket.NumeroSecuencial} vence el {ticket.FechaVencimiento:yyyy-MM-dd HH:mm} UTC.",
                    "AVISO", "TICKET", ticket.Id.ToString("D"), $"ticket:{ticket.Id:D}:TICKET_PROXIMO_A_VENCER",
                    new { ticketId = ticket.Id, ticket.NumeroSecuencial, fechaVencimientoUtc = ticket.FechaVencimiento }, cancellationToken);
                processed++;
            }
            else if (state == EstadoTicket.VENCIDO)
            {
                await notifications.CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR"], "TICKET_VENCIDO",
                    "Ticket vencido", $"El ticket {ticket.NumeroSecuencial} venció el {ticket.FechaVencimiento:yyyy-MM-dd HH:mm} UTC.",
                    "CRITICA", "TICKET", ticket.Id.ToString("D"), $"ticket:{ticket.Id:D}:TICKET_VENCIDO",
                    new { ticketId = ticket.Id, ticket.NumeroSecuencial, fechaVencimientoUtc = ticket.FechaVencimiento }, cancellationToken);
                processed++;
            }
        }
        return processed;
    }
}
