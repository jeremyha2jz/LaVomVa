using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/programaciones")]
[Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
public sealed class ProgramacionesController(SolicitudProgramacionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken) => Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetAsync(id, cancellationToken)); }
        catch (ProgramacionSolicitudNoEncontrada ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
    }

    [HttpGet("{id:long}/ejecuciones")]
    public async Task<IActionResult> Ejecuciones(long id, CancellationToken cancellationToken)
    {
        try { return Ok(await service.HistoryAsync(id, cancellationToken)); }
        catch (ProgramacionSolicitudNoEncontrada ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearProgramacionSolicitudRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out var actorId)) return Unauthorized();
        try
        {
            var result = await service.CreateAsync(request, actorId, cancellationToken);
            return CreatedAtAction(nameof(Obtener), new { id = result.Id }, result);
        }
        catch (ProgramacionSolicitudInvalida ex) { return BadRequest(new ApiErrorResponse(ex.Message)); }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, CrearProgramacionSolicitudRequest request, CancellationToken cancellationToken)
    {
        if (!TryActor(out var actorId)) return Unauthorized();
        try { return Ok(await service.UpdateAsync(id, request, actorId, cancellationToken)); }
        catch (ProgramacionSolicitudNoEncontrada ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
        catch (ProgramacionSolicitudInvalida ex) { return BadRequest(new ApiErrorResponse(ex.Message)); }
        catch (ProgramacionSolicitudConflicto ex) { return Conflict(new ApiErrorResponse(ex.Message)); }
    }

    [HttpPost("{id:long}/activar")]
    public Task<IActionResult> Activar(long id, CancellationToken cancellationToken) => SetActive(id, true, cancellationToken);

    [HttpPost("{id:long}/desactivar")]
    public Task<IActionResult> Desactivar(long id, CancellationToken cancellationToken) => SetActive(id, false, cancellationToken);

    private async Task<IActionResult> SetActive(long id, bool active, CancellationToken cancellationToken)
    {
        if (!TryActor(out var actorId)) return Unauthorized();
        try { return Ok(await service.SetActiveAsync(id, active, actorId, cancellationToken)); }
        catch (ProgramacionSolicitudNoEncontrada ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
        catch (ProgramacionSolicitudInvalida ex) { return BadRequest(new ApiErrorResponse(ex.Message)); }
        catch (ProgramacionSolicitudConflicto ex) { return Conflict(new ApiErrorResponse(ex.Message)); }
    }

    private bool TryActor(out long actorId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorId);
}
