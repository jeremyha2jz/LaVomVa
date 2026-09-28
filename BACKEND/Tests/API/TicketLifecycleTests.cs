using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;
using Xunit;

namespace TicketsCombustible.Api.Tests;

public sealed class TicketLifecycleTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(EstadoTicket.CREADO)]
    [InlineData(EstadoTicket.ENVIADO)]
    [InlineData(EstadoTicket.PENDIENTE)]
    public void Conserva_estado_base_mientras_no_se_acerque_el_vencimiento(EstadoTicket baseState)
    {
        var policy = new TicketLifecycleService(TimeProvider.System);
        Assert.Equal(baseState, policy.EstadoActual(Ticket(baseState, Now.AddDays(3)), Now));
    }

    [Fact]
    public void Deriva_proximo_a_vencer_al_limite_de_dos_dias()
    {
        var policy = new TicketLifecycleService(TimeProvider.System);
        Assert.Equal(EstadoTicket.PROXIMO_A_VENCER,
            policy.EstadoActual(Ticket(EstadoTicket.CREADO, Now + TicketLifecycleService.ProximoAVencerThreshold), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deriva_vencido_al_instante_de_vencimiento_o_despues(int ticks)
    {
        var policy = new TicketLifecycleService(TimeProvider.System);
        var expiry = Now.AddTicks(ticks);
        Assert.Equal(EstadoTicket.VENCIDO, policy.EstadoActual(Ticket(EstadoTicket.CREADO, expiry), Now));
    }

    [Theory]
    [InlineData(EstadoTicket.CREADO, EstadoTicket.CONSUMIDO, true)]
    [InlineData(EstadoTicket.ENVIADO, EstadoTicket.CONSUMIDO, true)]
    [InlineData(EstadoTicket.PENDIENTE, EstadoTicket.CONSUMIDO, true)]
    [InlineData(EstadoTicket.PROXIMO_A_VENCER, EstadoTicket.CONSUMIDO, true)]
    [InlineData(EstadoTicket.VENCIDO, EstadoTicket.CONSUMIDO, false)]
    [InlineData(EstadoTicket.CREADO, EstadoTicket.ANULADO, true)]
    [InlineData(EstadoTicket.ENVIADO, EstadoTicket.ANULADO, true)]
    [InlineData(EstadoTicket.PENDIENTE, EstadoTicket.ANULADO, true)]
    [InlineData(EstadoTicket.PROXIMO_A_VENCER, EstadoTicket.ANULADO, true)]
    [InlineData(EstadoTicket.VENCIDO, EstadoTicket.ANULADO, true)]
    [InlineData(EstadoTicket.CONSUMIDO, EstadoTicket.ANULADO, false)]
    [InlineData(EstadoTicket.ANULADO, EstadoTicket.ANULADO, false)]
    [InlineData(EstadoTicket.CREADO, EstadoTicket.ENVIADO, false)]
    [InlineData(EstadoTicket.CREADO, EstadoTicket.PENDIENTE, true)]
    [InlineData(EstadoTicket.PROXIMO_A_VENCER, EstadoTicket.PENDIENTE, true)]
    [InlineData(EstadoTicket.ENVIADO, EstadoTicket.PENDIENTE, true)]
    [InlineData(EstadoTicket.PENDIENTE, EstadoTicket.ENVIADO, true)]
    [InlineData(EstadoTicket.ANULADO, EstadoTicket.PENDIENTE, false)]
    [InlineData(EstadoTicket.CONSUMIDO, EstadoTicket.PENDIENTE, false)]
    [InlineData(EstadoTicket.ANULADO, EstadoTicket.CREADO, false)]
    [InlineData(EstadoTicket.CONSUMIDO, EstadoTicket.CREADO, false)]
    public void Aplica_la_matriz_explicita_de_transiciones(EstadoTicket from, EstadoTicket to, bool allowed)
    {
        var policy = new TicketLifecycleService(TimeProvider.System);
        Assert.Equal(allowed, policy.PuedeTransicionar(from, to));
    }

    private static Ticket Ticket(EstadoTicket state, DateTime expiry) => new()
    {
        Estado = state,
        FechaVencimiento = expiry
    };
}
