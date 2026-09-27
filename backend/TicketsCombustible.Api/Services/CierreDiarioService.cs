using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public sealed record TanqueCierreResumen(
    long TanqueId,
    string Codigo,
    string Nombre,
    decimal CapacidadGalones,
    decimal InventarioInicialGalones,
    decimal EntradasGalones,
    decimal DespachadoGalones,
    decimal OtrasSalidasGalones,
    decimal MermasGalones,
    decimal AjustesGalones,
    decimal InventarioTeoricoFinalGalones);

public sealed record CierreDiarioResumen(
    long EstacionId,
    string Estacion,
    DateOnly Fecha,
    decimal InventarioInicialGalones,
    decimal VolumenRecibidoGalones,
    decimal VolumenDespachadoGalones,
    decimal OtrasSalidasGalones,
    decimal MermasGalones,
    decimal AjustesGalones,
    decimal InventarioTeoricoFinalGalones,
    int CantidadDespachos,
    IReadOnlyList<TanqueCierreResumen> Tanques);

public sealed class CierreIntegridadException(string message) : Exception(message);

/// <summary>Calcula cierres desde el libro de movimientos persistido. Todas las marcas de tiempo nuevas del inventario representan UTC.</summary>
public sealed class CierreDiarioService(TicketsCombustibleDbContext db)
{
    public async Task<CierreDiarioResumen?> CalcularAsync(long estacionId, DateOnly fecha, CancellationToken cancellationToken = default)
    {
        var estacion = await db.Estaciones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == estacionId, cancellationToken);
        if (estacion is null) return null;

        var inicio = fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var fin = fecha.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var tanques = await db.Tanques.AsNoTracking().Where(x => x.EstacionId == estacionId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var detalles = new List<TanqueCierreResumen>(tanques.Count);

        foreach (var tanque in tanques)
        {
            var anteriorAlDia = await db.MovimientosInventario.AsNoTracking()
                .Where(x => x.TanqueId == tanque.Id && x.FechaHora < inicio)
                .OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
                .Select(x => x.ExistenciaNueva).FirstOrDefaultAsync(cancellationToken);
            var movimientosDelDia = await db.MovimientosInventario.AsNoTracking()
                .Where(x => x.TanqueId == tanque.Id && x.FechaHora >= inicio && x.FechaHora < fin)
                .OrderBy(x => x.FechaHora).ThenBy(x => x.Id).ToListAsync(cancellationToken);
            var primeroDespues = movimientosDelDia.Count == 0
                ? await db.MovimientosInventario.AsNoTracking()
                    .Where(x => x.TanqueId == tanque.Id && x.FechaHora >= fin)
                    .OrderBy(x => x.FechaHora).ThenBy(x => x.Id)
                    .Select(x => x.ExistenciaAnterior).FirstOrDefaultAsync(cancellationToken)
                : null;

            var inventarioInicial = anteriorAlDia ?? movimientosDelDia.FirstOrDefault()?.ExistenciaAnterior ?? primeroDespues ?? tanque.ExistenciaActualGalones;
            var entradas = movimientosDelDia.Where(x => x.TipoMovimiento == "ENTRADA").Sum(x => x.CantidadGalones);
            var despachos = movimientosDelDia.Where(x => x.TipoMovimiento == "SALIDA" && x.ReferenciaTipo == "DESPACHO").Sum(x => x.CantidadGalones);
            var otrasSalidas = movimientosDelDia.Where(x => x.TipoMovimiento is "SALIDA" or "TRANSFERENCIA_SALIDA")
                .Where(x => x.TipoMovimiento != "SALIDA" || x.ReferenciaTipo != "DESPACHO").Sum(x => x.CantidadGalones);
            var mermas = movimientosDelDia.Where(x => x.TipoMovimiento == "MERMA").Sum(x => x.CantidadGalones);
            var ajustesPositivos = movimientosDelDia.Where(x => x.TipoMovimiento == "AJUSTE_POSITIVO").Sum(x => x.CantidadGalones);
            var ajustesNegativos = movimientosDelDia.Where(x => x.TipoMovimiento == "AJUSTE_NEGATIVO").Sum(x => x.CantidadGalones);
            var ajustes = ajustesPositivos - ajustesNegativos;
            var cambioNeto = movimientosDelDia.Sum(CambioFirmado);
            var inventarioTeorico = inventarioInicial + cambioNeto;

            var inventarioFinalDelLibro = movimientosDelDia.LastOrDefault()?.ExistenciaNueva ?? primeroDespues ?? inventarioInicial;
            if (inventarioTeorico != inventarioFinalDelLibro)
                throw new CierreIntegridadException($"El libro de movimientos del tanque {tanque.Codigo} no concilia para {fecha:yyyy-MM-dd}.");

            detalles.Add(new TanqueCierreResumen(tanque.Id, tanque.Codigo, tanque.Nombre ?? tanque.Codigo,
                tanque.CapacidadGalones, inventarioInicial, entradas, despachos, otrasSalidas, mermas, ajustes, inventarioTeorico));
        }

        var movimientoDespachos = detalles.Sum(x => x.DespachadoGalones);
        var conteoMovimientosDespacho = await (from movimiento in db.MovimientosInventario.AsNoTracking()
            join tanque in db.Tanques.AsNoTracking() on movimiento.TanqueId equals tanque.Id
            where tanque.EstacionId == estacionId && movimiento.TipoMovimiento == "SALIDA"
                && movimiento.ReferenciaTipo == "DESPACHO" && movimiento.FechaHora >= inicio && movimiento.FechaHora < fin
            select movimiento.Id).CountAsync(cancellationToken);
        var despachosPersistidos = await db.Despachos.AsNoTracking()
            .Where(x => x.EstacionId == estacionId && x.FechaHora >= inicio && x.FechaHora < fin)
            .CountAsync(cancellationToken);
        if (conteoMovimientosDespacho != despachosPersistidos)
            throw new CierreIntegridadException("El conteo de despachos no coincide con el libro de movimientos de inventario.");

        return new CierreDiarioResumen(estacionId, estacion.Nombre, fecha,
            detalles.Sum(x => x.InventarioInicialGalones),
            detalles.Sum(x => x.EntradasGalones),
            movimientoDespachos,
            detalles.Sum(x => x.OtrasSalidasGalones),
            detalles.Sum(x => x.MermasGalones),
            detalles.Sum(x => x.AjustesGalones),
            detalles.Sum(x => x.InventarioTeoricoFinalGalones),
            despachosPersistidos, detalles);
    }

    public static decimal CambioFirmado(MovimientoInventario movimiento) => movimiento.TipoMovimiento switch
    {
        "ENTRADA" or "AJUSTE_POSITIVO" or "TRANSFERENCIA_ENTRADA" => movimiento.CantidadGalones,
        _ => -movimiento.CantidadGalones
    };
}
