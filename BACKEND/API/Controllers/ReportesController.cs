using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/reportes")]
[Authorize]
public sealed class ReportesController(ReportesService reportes) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ReporteResultado), StatusCodes.Status200OK)]
    public async Task<IActionResult> Consultar(
        [FromQuery] string tipo = "consumo",
        [FromQuery] DateOnly? desde = null,
        [FromQuery] DateOnly? hasta = null,
        [FromQuery] long? departamentoId = null,
        [FromQuery] long? combustibleId = null,
        [FromQuery] long? empleadoId = null,
        [FromQuery] long? vehiculoId = null,
        [FromQuery] string? estado = null,
        [FromQuery] long? estacionId = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtros = new ReporteFiltros(tipo, desde, hasta, departamentoId, combustibleId, empleadoId,
                vehiculoId, estado, estacionId, pagina, tamanoPagina);
            return Ok(await reportes.ConsultarAsync(filtros, ct: cancellationToken));
        }
        catch (ReporteFiltroException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }

    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar(
        [FromQuery] string formato,
        [FromQuery] string tipo = "consumo",
        [FromQuery] DateOnly? desde = null,
        [FromQuery] DateOnly? hasta = null,
        [FromQuery] long? departamentoId = null,
        [FromQuery] long? combustibleId = null,
        [FromQuery] long? empleadoId = null,
        [FromQuery] long? vehiculoId = null,
        [FromQuery] string? estado = null,
        [FromQuery] long? estacionId = null,
        CancellationToken cancellationToken = default)
    {
        if (formato is not ("csv" or "xlsx" or "pdf"))
            return BadRequest(new { mensaje = "El formato debe ser csv, xlsx o pdf." });
        try
        {
            var filtros = new ReporteFiltros(tipo, desde, hasta, departamentoId, combustibleId, empleadoId,
                vehiculoId, estado, estacionId, 1, ReportesService.MaxExportRows);
            var report = await reportes.ConsultarAsync(filtros, exportacion: true, cancellationToken);
            var file = formato switch
            {
                "csv" => (ReportesExportador.Csv(report), "text/csv; charset=utf-8"),
                "xlsx" => (ReportesExportador.Xlsx(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                _ => (ReportesExportador.Pdf(report), "application/pdf")
            };
            var stamp = report.GeneradoEnUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return File(file.Item1, file.Item2, $"reporte-{report.Tipo}-{stamp}.{formato}");
        }
        catch (ReporteFiltroException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }
}
