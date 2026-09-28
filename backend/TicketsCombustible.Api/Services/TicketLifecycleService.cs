using TicketsCombustible.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace TicketsCombustible.Api.Services;

/// <summary>Single application policy for persisted and time-derived ticket states.</summary>
public sealed class TicketLifecycleService(TimeProvider timeProvider)
{
    // Reuses the two-day warning threshold already used by vw_tickets_alerta.
    public static readonly TimeSpan ProximoAVencerThreshold = TimeSpan.FromDays(2);

    public DateTime UtcNow => DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);

    public EstadoTicket EstadoActual(Ticket ticket) => EstadoActual(ticket, UtcNow);

    public EstadoTicket EstadoActual(Ticket ticket, DateTime instanteUtc)
    {
        if (ticket.Estado is EstadoTicket.CONSUMIDO or EstadoTicket.ANULADO) return ticket.Estado;

        var ahora = ComoUtc(instanteUtc);
        var vence = ComoUtc(ticket.FechaVencimiento);
        if (vence <= ahora) return EstadoTicket.VENCIDO;
        if (vence <= ahora + ProximoAVencerThreshold) return EstadoTicket.PROXIMO_A_VENCER;

        return ticket.Estado is EstadoTicket.CREADO or EstadoTicket.ENVIADO or EstadoTicket.PENDIENTE
            ? ticket.Estado
            : EstadoTicket.CREADO;
    }

    /// <summary>Applies the same persisted and time-derived state rules to database queries.</summary>
    public IQueryable<Ticket> FiltrarPorEstado(IQueryable<Ticket> tickets, EstadoTicket estado, DateTime? instanteUtc = null)
    {
        var now = ComoUtc(instanteUtc ?? UtcNow);
        var soon = now.Add(ProximoAVencerThreshold);
        return estado switch
        {
            EstadoTicket.VENCIDO => tickets.Where(t => t.Estado != EstadoTicket.CONSUMIDO && t.Estado != EstadoTicket.ANULADO && t.FechaVencimiento <= now),
            EstadoTicket.PROXIMO_A_VENCER => tickets.Where(t => t.Estado != EstadoTicket.CONSUMIDO && t.Estado != EstadoTicket.ANULADO && t.FechaVencimiento > now && t.FechaVencimiento <= soon),
            EstadoTicket.CREADO => tickets.Where(t => (t.Estado == EstadoTicket.CREADO || t.Estado == EstadoTicket.PROXIMO_A_VENCER || t.Estado == EstadoTicket.VENCIDO) && t.FechaVencimiento > soon),
            EstadoTicket.ENVIADO => tickets.Where(t => t.Estado == EstadoTicket.ENVIADO && t.FechaVencimiento > soon),
            EstadoTicket.PENDIENTE => tickets.Where(t => t.Estado == EstadoTicket.PENDIENTE && t.FechaVencimiento > soon),
            _ => tickets.Where(t => t.Estado == estado)
        };
    }

    public bool PuedeTransicionar(EstadoTicket estadoActual, EstadoTicket estadoDestino) => estadoDestino switch
    {
        EstadoTicket.CONSUMIDO => estadoActual is EstadoTicket.CREADO or EstadoTicket.ENVIADO or EstadoTicket.PENDIENTE or EstadoTicket.PROXIMO_A_VENCER,
        // Expired tickets may still be administratively voided; consumed/voided tickets are terminal.
        EstadoTicket.ANULADO => estadoActual is EstadoTicket.CREADO or EstadoTicket.ENVIADO or EstadoTicket.PENDIENTE or EstadoTicket.PROXIMO_A_VENCER or EstadoTicket.VENCIDO,
        EstadoTicket.PENDIENTE => estadoActual is EstadoTicket.CREADO or EstadoTicket.ENVIADO or EstadoTicket.PENDIENTE or EstadoTicket.PROXIMO_A_VENCER,
        EstadoTicket.ENVIADO => estadoActual == EstadoTicket.PENDIENTE,
        _ => false
    };

    public static DateTime ComoUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
