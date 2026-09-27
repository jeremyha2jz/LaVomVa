using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;
using Xunit;

namespace TicketsCombustible.Api.Tests;

[Collection("QA database")]
public sealed class ApiCoverageTests(QaFixture qa)
{
    private HttpClient Client => qa.Client;

    [Fact]
    public async Task Usuarios_crear_editar_rol_desactivar_activar_restaurar_y_validar_entradas()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.admin.users", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var roles = await GetRolesAsync();
        var consulta = roles["CONSULTA"];
        var solicitante = roles["SOLICITANTE"];
        var despachador = roles["DESPACHADOR"];
        var supervisor = roles["SUPERVISOR"];

        var created = await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.target", "qa.target@example.test", "Persona QA", "Una-clave-qa-larga-2026", solicitante));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var target = await created.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetInt64();
        Assert.DoesNotContain("password_hash", (await created.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.target", "qa.other@example.test", "Otra Persona", ApiTestFactory.TestPassword, consulta))).StatusCode);
        var other = await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.other", "qa.other@example.test", "Otra Persona", ApiTestFactory.TestPassword, consulta));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        _ = (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.unique", "qa.other@example.test", "Otra Persona", ApiTestFactory.TestPassword, consulta))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.short", "short@example.test", "Persona", "short", consulta))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("  ", "no-es-correo", "  ", ApiTestFactory.TestPassword, consulta))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/usuarios", new { nombreUsuario = (string?)null, correo = "null@example.test", nombreCompleto = "Nombre", password = ApiTestFactory.TestPassword, rolId = consulta })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.badrole", "badrole@example.test", "Nombre", ApiTestFactory.TestPassword, 999999))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/gestion/usuarios/{targetId}", new { correo = "invalido", nombreCompleto = "Nombre", rolId = despachador })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/gestion/usuarios/{targetId}", new { correo = "qa.target@example.test", nombreCompleto = "Nombre", rolId = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"api/gestion/usuarios/{targetId}", new { correo = "qa.other@example.test", nombreCompleto = "Nombre", rolId = despachador })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/gestion/usuarios/999999", new { correo = "missing@example.test", nombreCompleto = "Nombre", rolId = despachador })).StatusCode);
        var updated = await Client.PutAsJsonAsync($"api/gestion/usuarios/{targetId}", new { correo = "QA.TARGET@EXAMPLE.TEST", nombreCompleto = "Persona QA Actualizada", telefono = "8095550101", rolId = despachador });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal("qa.target@example.test", (await db.Usuarios.SingleAsync(x => x.Id == targetId)).Correo);
            Assert.Equal(despachador, await db.UsuarioRoles.Where(x => x.UsuarioId == targetId).Select(x => x.RolId).SingleAsync());
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"api/gestion/usuarios/{targetId}/restablecer-contrasena", new { contrasena = "corta" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsJsonAsync("api/gestion/usuarios/999999/restablecer-contrasena", new { contrasena = ApiTestFactory.TestPassword })).StatusCode);
        const string resetPassword = "Nueva-clave-QA-2026";
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync($"api/gestion/usuarios/{targetId}/restablecer-contrasena", new { contrasena = resetPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login", new { usuario = "qa.target", contrasena = ApiTestFactory.TestPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/usuarios/{targetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync("api/login", new { usuario = "qa.target", contrasena = resetPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync($"api/gestion/usuarios/{targetId}/activar", null)).StatusCode);
        var activeLogin = await Client.PostAsJsonAsync("api/login", new { usuario = "qa.target", contrasena = resetPassword });
        Assert.Equal(HttpStatusCode.OK, activeLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"api/gestion/usuarios/{admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"api/gestion/usuarios/{admin.Id}", new { correo = "qa.admin.new@example.test", nombreCompleto = "Admin", rolId = supervisor })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("api/gestion/usuarios/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync("api/gestion/usuarios/999999/activar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("api/gestion/usuarios")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Theory]
    [InlineData("ADMINISTRADOR", "api/notificaciones", "GET", 200)]
    [InlineData("SUPERVISOR", "api/notificaciones", "GET", 200)]
    [InlineData("DESPACHADOR", "api/notificaciones", "GET", 200)]
    [InlineData("AUDITOR", "api/notificaciones", "GET", 200)]
    [InlineData("CONSULTA", "api/notificaciones", "GET", 403)]
    [InlineData("SOLICITANTE", "api/notificaciones", "GET", 403)]
    [InlineData("ADMINISTRADOR", "api/notificaciones/no-leidas", "GET", 200)]
    [InlineData("CONSULTA", "api/notificaciones/no-leidas", "GET", 403)]
    [InlineData("ADMINISTRADOR", "api/tickets/qr", "GET", 200)]
    [InlineData("SUPERVISOR", "api/tickets/qr", "GET", 200)]
    [InlineData("DESPACHADOR", "api/tickets/qr", "GET", 403)]
    [InlineData("AUDITOR", "api/tickets/qr", "GET", 403)]
    [InlineData("CONSULTA", "api/tickets/qr", "GET", 403)]
    [InlineData("SOLICITANTE", "api/tickets/qr", "GET", 403)]
    [InlineData("ADMINISTRADOR", "api/tickets", "POST", 201)]
    [InlineData("SUPERVISOR", "api/tickets", "POST", 201)]
    [InlineData("DESPACHADOR", "api/tickets", "POST", 403)]
    [InlineData("AUDITOR", "api/tickets", "POST", 403)]
    [InlineData("CONSULTA", "api/tickets", "POST", 403)]
    [InlineData("SOLICITANTE", "api/tickets", "POST", 403)]
    [InlineData("ADMINISTRADOR", "api/tickets/{id}/enviar", "POST", 200)]
    [InlineData("SUPERVISOR", "api/tickets/{id}/enviar", "POST", 200)]
    [InlineData("DESPACHADOR", "api/tickets/{id}/enviar", "POST", 403)]
    [InlineData("AUDITOR", "api/tickets/{id}/enviar", "POST", 403)]
    [InlineData("CONSULTA", "api/tickets/{id}/enviar", "POST", 403)]
    [InlineData("SOLICITANTE", "api/tickets/{id}/enviar", "POST", 403)]
    [InlineData("ADMINISTRADOR", "api/tickets/{id}/envios", "GET", 200)]
    [InlineData("SUPERVISOR", "api/tickets/{id}/envios", "GET", 200)]
    [InlineData("DESPACHADOR", "api/tickets/{id}/envios", "GET", 403)]
    [InlineData("AUDITOR", "api/tickets/{id}/envios", "GET", 403)]
    [InlineData("CONSULTA", "api/tickets/{id}/envios", "GET", 403)]
    [InlineData("SOLICITANTE", "api/tickets/{id}/envios", "GET", 403)]
    [InlineData("ADMINISTRADOR", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 404)]
    [InlineData("SUPERVISOR", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 404)]
    [InlineData("DESPACHADOR", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 403)]
    [InlineData("AUDITOR", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 403)]
    [InlineData("CONSULTA", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 403)]
    [InlineData("SOLICITANTE", "api/tickets/{id}/envios/{envioId}/reconciliar", "POST", 403)]
    [InlineData("ADMINISTRADOR", "api/despachos", "POST", 200)]
    [InlineData("SUPERVISOR", "api/despachos", "POST", 403)]
    [InlineData("DESPACHADOR", "api/despachos", "POST", 200)]
    [InlineData("AUDITOR", "api/despachos", "POST", 403)]
    [InlineData("CONSULTA", "api/despachos", "POST", 403)]
    [InlineData("SOLICITANTE", "api/despachos", "POST", 403)]
    [InlineData("ADMINISTRADOR", "api/programaciones", "GET", 200)]
    [InlineData("SUPERVISOR", "api/programaciones", "GET", 200)]
    [InlineData("DESPACHADOR", "api/programaciones", "GET", 403)]
    [InlineData("AUDITOR", "api/programaciones", "GET", 403)]
    [InlineData("CONSULTA", "api/programaciones", "GET", 403)]
    [InlineData("SOLICITANTE", "api/programaciones", "GET", 403)]
    public async Task Matriz_RBAC_roles_por_endpoint(string role, string path, string method, int expectedStatus)
    {
        await qa.ResetAsync();
        var admin = await LoginAsync($"qa.rbac.admin.{role.ToLowerInvariant()}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 100m);
        var existingTicket = await IssueTicketAsync(catalog, 10m);
        var pendingIssueRequest = await CreateApprovedRequestAsync(catalog, 10m);
        var identity = await LoginAsync($"qa.role.{role.ToLowerInvariant()}", role);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.Token);
        var url = path switch
        {
            "api/tickets/qr" => $"api/tickets/{existingTicket.Id}/qr",
            "api/tickets/{id}/enviar" => $"api/tickets/{existingTicket.Id}/enviar",
            "api/tickets/{id}/envios" => $"api/tickets/{existingTicket.Id}/envios",
            "api/tickets/{id}/envios/{envioId}/reconciliar" => $"api/tickets/{existingTicket.Id}/envios/999999/reconciliar",
            _ => path
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (path == "api/tickets") request.Content = JsonContent.Create(new { solicitudId = pendingIssueRequest });
        if (path == "api/tickets/{id}/enviar") request.Content = JsonContent.Create(new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() });
        if (path == "api/tickets/{id}/envios/{envioId}/reconciliar") request.Content = JsonContent.Create(new { estado = "ENVIADO" });
        if (path == "api/despachos")
        {
            var validation = await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = existingTicket.Token });
            Assert.True((await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
            request.Content = JsonContent.Create(new { ticketId = existingTicket.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true });
        }
        using var response = await Client.SendAsync(request);
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
        _ = admin;
    }

    [Fact]
    public async Task Programacion_recurrente_genera_solicitud_pendiente_auditoria_y_salta_periodos_atrasados()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.schedule.recurrent", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start);
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "RECURRENTE", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 12.5m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = "DIARIA"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var schedule = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = schedule.GetProperty("id").GetInt64();
        qa.Factory.Clock.SetUtcNow(start.AddDays(5).AddHours(2));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>();
            Assert.Equal(1, await processor.ProcessDueAsync(CancellationToken.None));
        }
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var run = await db.EjecucionesProgramadas.SingleAsync(x => x.ProgramacionId == id);
            var generated = await db.Solicitudes.SingleAsync(x => x.Id == run.SolicitudGeneradaId);
            var program = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            Assert.Equal("GENERADA", run.Estado);
            Assert.Equal(EstadoSolicitud.PENDIENTE, generated.Estado);
            Assert.Equal("RECURRENTE", generated.TipoSolicitud);
            Assert.Equal("DIARIA", generated.Frecuencia);
            Assert.Equal(12.5m, generated.CantidadSolicitadaGalones);
            Assert.Equal(start.AddDays(6).UtcDateTime, DateTime.SpecifyKind(program.ProximaEjecucion!.Value, DateTimeKind.Utc));
            Assert.Contains(await db.Auditoria.ToListAsync(), x => x.Accion == "SOLICITUD_RECURRENTE_GENERADA");
        }
        var history = await Client.GetAsync($"api/programaciones/{id}/ejecuciones");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyRows = await history.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(historyRows);
        Assert.Single(historyRows);
        Assert.Equal(admin.Id, schedule.GetProperty("usuarioCreadorId").GetInt64());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Programacion_automatica_es_unica_y_dos_workers_no_generan_doble_solicitud()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.race", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "AUTOMATICA", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 7m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        qa.Factory.Clock.SetUtcNow(start);
        async Task<int> RunWorkerAsync()
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None);
        }
        var results = await Task.WhenAll(RunWorkerAsync(), RunWorkerAsync());
        Assert.Equal(1, results.Sum());
        Assert.Equal(0, await RunWorkerAsync());
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var program = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            Assert.False(program.Activa);
            Assert.Null(program.ProximaEjecucion);
            Assert.Single(await db.EjecucionesProgramadas.Where(x => x.ProgramacionId == id).ToListAsync());
            var request = await db.Solicitudes.SingleAsync(x => x.TipoSolicitud == "AUTOMATICA");
            Assert.Equal(EstadoSolicitud.PENDIENTE, request.Estado);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"api/programaciones/{id}/activar", null)).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Programacion_valida_referencias_frecuencia_y_autenticacion_y_pausa_asociaciones_invalidas()
    {
        await qa.ResetAsync();
        var catalog = await SeedCatalogAsync();
        var payload = new CrearProgramacionSolicitudRequest("RECURRENTE", catalog.EmployeeId, catalog.VehicleId,
            catalog.DepartmentId, catalog.FuelId, 4m, DateTimeOffset.UtcNow.AddDays(1), null, "QUINCENAL");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/programaciones", payload)).StatusCode);
        _ = await LoginAsync("qa.schedule.invalid", "ADMINISTRADOR");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/programaciones", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/programaciones", payload with { Frecuencia = "DIARIA", CantidadSolicitadaGalones = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/programaciones", payload with { Frecuencia = "DIARIA", CantidadSolicitadaGalones = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/programaciones", payload with { Frecuencia = "DIARIA", FechaFinal = payload.FechaInicial.AddDays(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/programaciones", payload with { TipoSolicitud = "MANUAL", Frecuencia = null })).StatusCode);
        var valid = await Client.PostAsJsonAsync("api/programaciones", payload with { Frecuencia = "DIARIA" });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var id = (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var employee = await db.Empleados.SingleAsync(x => x.Id == catalog.EmployeeId);
            employee.Activo = false;
            await db.SaveChangesAsync();
        }
        qa.Factory.Clock.SetUtcNow(payload.FechaInicial.AddMinutes(1));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var schedule = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            var failure = await db.EjecucionesProgramadas.SingleAsync(x => x.ProgramacionId == id);
            Assert.False(schedule.Activa);
            Assert.Equal("FALLIDA", failure.Estado);
            Assert.NotNull(failure.DetalleError);
            Assert.Empty(await db.Solicitudes.Where(x => x.TipoSolicitud == "RECURRENTE").ToListAsync());
            Assert.Contains(await db.Auditoria.ToListAsync(), x => x.Accion == "EJECUCION_PROGRAMADA_FALLIDA");
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Programacion_mensual_conserva_el_ancla_en_fin_de_mes_y_ano_bisiesto()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.monthly", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2024, 1, 31, 10, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var response = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "RECURRENTE", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 9m, fechaInicial = start, fechaFinal = start.AddMonths(2), frecuencia = "MENSUAL"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        qa.Factory.Clock.SetUtcNow(start);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(start.AddMonths(1).UtcDateTime, DateTime.SpecifyKind((await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id)).ProximaEjecucion!.Value, DateTimeKind.Utc));
        }
        qa.Factory.Clock.SetUtcNow(start.AddMonths(1));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var program = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            Assert.Equal(new DateTime(2024, 3, 31, 10, 0, 0), program.ProximaEjecucion);
            Assert.Equal(2, await db.EjecucionesProgramadas.CountAsync(x => x.ProgramacionId == id));
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Programacion_se_desactiva_al_ejecutar_su_ultima_fecha_permitida()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.until", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var response = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "RECURRENTE", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 9m, fechaInicial = start, fechaFinal = start, frecuencia = "SEMANAL"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        qa.Factory.Clock.SetUtcNow(start);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var program = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            Assert.False(program.Activa);
            Assert.Null(program.ProximaEjecucion);
            Assert.Single(await db.EjecucionesProgramadas.Where(x => x.ProgramacionId == id).ToListAsync());
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Programacion_automatica_fallida_se_puede_reactivar_tras_corregir_referencia()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.retry", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "AUTOMATICA", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 5m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            (await db.Empleados.SingleAsync(x => x.Id == catalog.EmployeeId)).Activo = false;
            await db.SaveChangesAsync();
        }
        qa.Factory.Clock.SetUtcNow(start);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            (await db.Empleados.SingleAsync(x => x.Id == catalog.EmployeeId)).Activo = true;
            await db.SaveChangesAsync();
        }
        var activated = await Client.PostAsync($"api/programaciones/{id}/activar", null);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var retryAt = (await activated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proximaEjecucion").GetDateTimeOffset();
        qa.Factory.Clock.SetUtcNow(retryAt);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(new[] { "FALLIDA", "GENERADA" }, await db.EjecucionesProgramadas.Where(x => x.ProgramacionId == id).OrderBy(x => x.Id).Select(x => x.Estado).ToArrayAsync());
            Assert.Single(await db.Solicitudes.Where(x => x.TipoSolicitud == "AUTOMATICA").ToListAsync());
            Assert.False((await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id)).Activa);
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Theory]
    [InlineData("vehiculo")]
    [InlineData("departamento")]
    public async Task Programacion_pausada_con_referencia_inactiva(string reference)
    {
        await qa.ResetAsync();
        _ = await LoginAsync($"qa.schedule.inactive.{reference}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "RECURRENTE", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 3m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = "DIARIA"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            if (reference == "vehiculo") (await db.Vehiculos.SingleAsync(x => x.Id == catalog.VehicleId)).Activo = false;
            else (await db.Departamentos.SingleAsync(x => x.Id == catalog.DepartmentId)).Activo = false;
            await db.SaveChangesAsync();
        }
        qa.Factory.Clock.SetUtcNow(start);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.False((await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id)).Activa);
            Assert.Equal("FALLIDA", (await db.EjecucionesProgramadas.SingleAsync(x => x.ProgramacionId == id)).Estado);
            Assert.Empty(await db.Solicitudes.Where(x => x.TipoSolicitud == "RECURRENTE").ToListAsync());
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public void Siguiente_ejecucion_respeta_limites_UTC_y_ancla_mensual_al_dia_original()
    {
        var dailyStart = new DateTime(2026, 9, 27, 23, 59, 59, DateTimeKind.Utc);
        var midnight = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
        var nextDaily = SolicitudProgramacionService.NextOccurrenceAtOrAfter(dailyStart, "DIARIA", midnight);
        Assert.Equal(new DateTime(2026, 9, 28, 23, 59, 59), nextDaily);
        var monthlyStart = new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc);
        var afterFebruary = new DateTime(2026, 2, 28, 10, 0, 1, DateTimeKind.Utc);
        var nextMonthly = SolicitudProgramacionService.NextOccurrenceAtOrAfter(monthlyStart, "MENSUAL", afterFebruary);
        Assert.Equal(new DateTime(2026, 3, 31, 10, 0, 0), nextMonthly);
        var leapStart = new DateTime(2024, 1, 31, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2024, 2, 29, 10, 0, 0), SolicitudProgramacionService.NextOccurrenceAtOrAfter(leapStart, "MENSUAL", leapStart.AddTicks(1)));
        var yearEnd = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2027, 1, 31, 23, 59, 59), SolicitudProgramacionService.NextOccurrenceAtOrAfter(yearEnd, "MENSUAL", yearEnd.AddSeconds(1)));
    }

    [Fact]
    public async Task Programacion_pausada_no_ejecuta_y_reactivacion_calcula_desde_el_momento_actual()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.pause", "SUPERVISOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-10));
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "RECURRENTE", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 4m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = "SEMANAL"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"api/programaciones/{id}/desactivar", null)).StatusCode);
        qa.Factory.Clock.SetUtcNow(start.AddDays(10));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        var activated = await Client.PostAsync($"api/programaciones/{id}/activar", null);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var dto = await activated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(start.AddDays(14).ToString("O"), dto.GetProperty("proximaEjecucion").GetDateTimeOffset().ToString("O"));
        qa.Factory.Clock.SetUtcNow(start.AddDays(14));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>().ProcessDueAsync(CancellationToken.None));
        var editedBody = new CrearProgramacionSolicitudRequest("RECURRENTE", catalog.EmployeeId, catalog.VehicleId,
            catalog.DepartmentId, catalog.FuelId, 6m, start.AddDays(12), null, "SEMANAL");
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/programaciones/{id}", editedBody)).StatusCode);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(4m, (await db.Solicitudes.SingleAsync(x => x.TipoSolicitud == "RECURRENTE")).CantidadSolicitadaGalones);
            Assert.Equal(6m, (await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id)).CantidadSolicitadaGalones);
            Assert.Contains(await db.Auditoria.ToListAsync(), x => x.Accion == "PROGRAMACION_DESACTIVADA");
            Assert.Contains(await db.Auditoria.ToListAsync(), x => x.Accion == "PROGRAMACION_ACTIVADA");
            Assert.Contains(await db.Auditoria.ToListAsync(), x => x.Accion == "PROGRAMACION_MODIFICADA");
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Fallo_de_auditoria_revierte_solicitud_historial_y_proxima_ejecucion_programada()
    {
        await qa.ResetAsync();
        _ = await LoginAsync("qa.schedule.rollback", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var start = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        qa.Factory.Clock.SetUtcNow(start.AddMinutes(-1));
        var create = await Client.PostAsJsonAsync("api/programaciones", new
        {
            tipoSolicitud = "AUTOMATICA", empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId,
            departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId,
            cantidadSolicitadaGalones = 5m, fechaInicial = start, fechaFinal = (DateTimeOffset?)null, frecuencia = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION qa_reject_schedule_audit() RETURNS TRIGGER AS $$
                BEGIN IF NEW.accion = 'SOLICITUD_AUTOMATICA_GENERADA' THEN RAISE EXCEPTION 'forced scheduler audit failure'; END IF; RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER qa_reject_schedule_audit BEFORE INSERT ON auditoria FOR EACH ROW EXECUTE FUNCTION qa_reject_schedule_audit();
                """);
        }
        qa.Factory.Clock.SetUtcNow(start);
        try
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>();
            await Assert.ThrowsAnyAsync<Exception>(() => processor.ProcessDueAsync(CancellationToken.None));
        }
        finally
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS qa_reject_schedule_audit ON auditoria; DROP FUNCTION IF EXISTS qa_reject_schedule_audit();");
        }
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var program = await db.ProgramacionesSolicitud.SingleAsync(x => x.Id == id);
            Assert.True(program.Activa);
            Assert.Equal(start.UtcDateTime, DateTime.SpecifyKind(program.ProximaEjecucion!.Value, DateTimeKind.Utc));
            Assert.Empty(await db.EjecucionesProgramadas.ToListAsync());
            Assert.Empty(await db.Solicitudes.Where(x => x.TipoSolicitud == "AUTOMATICA").ToListAsync());
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Catalogos_departamentos_empleados_y_vehiculos_validan_duplicados_edicion_y_bajas_logicas()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.admin.catalogs", "ADMINISTRADOR");
        var badDepartment = await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "", nombre = "   ", activo = true });
        Assert.Equal(HttpStatusCode.BadRequest, badDepartment.StatusCode);
        var firstDepartmentResponse = await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA-CAT-1", nombre = "QA Catalog One", activo = true });
        Assert.Equal(HttpStatusCode.Created, firstDepartmentResponse.StatusCode);
        var firstDepartmentId = (await firstDepartmentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA-CAT-1", nombre = "QA Catalog Duplicate Code", activo = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA-CAT-X", nombre = "QA Catalog One", activo = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/gestion/departamentos/999999", new { codigo = "QA-M", nombre = "Missing", activo = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/gestion/departamentos/{firstDepartmentId}", new { codigo = "QA-M", nombre = " ", activo = true })).StatusCode);
        var updateDepartment = await Client.PutAsJsonAsync($"api/gestion/departamentos/{firstDepartmentId}", new { codigo = "QA-CAT-1U", nombre = "QA Catalog Updated", descripcion = "Updated by test", activo = true });
        Assert.Equal(HttpStatusCode.OK, updateDepartment.StatusCode);
        var secondDepartment = await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA-CAT-2", nombre = "QA Catalog Two", activo = true });
        var secondDepartmentId = (await secondDepartment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"api/gestion/departamentos/{secondDepartmentId}", new { codigo = "QA-CAT-1U", nombre = "Different", activo = true })).StatusCode);

        var invalidDepartmentEmployee = Empleado("QA-DEPFAIL", "QA Employee Fail", "123-1111111-1", 999999);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/empleados", invalidDepartmentEmployee)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/empleados", Empleado(" ", "Name", "123-1111111-2", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/empleados", new { codigoEmpleado = "QA-EMAIL", nombreCompleto = "Email Invalid", cedula = "123-1111111-3", departamentoId = firstDepartmentId, correo = "not-mail", activo = true })).StatusCode);
        var empResponse = await Client.PostAsJsonAsync("api/gestion/empleados", Empleado("QA-CAT-E1", "Employee One", "123-1111111-4", firstDepartmentId));
        Assert.Equal(HttpStatusCode.Created, empResponse.StatusCode);
        var employeeId = (await empResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/empleados", Empleado("QA-CAT-E1", "Other Employee", "123-1111111-5", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/empleados", Empleado("QA-CAT-E2", "Other Employee", "123-1111111-4", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/gestion/empleados/{employeeId}", Empleado("QA-CAT-E1U", "Employee", "123-1111111-4", 999999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/gestion/empleados/999999", Empleado("QA-CAT-E1U", "Employee", "123-1111111-4", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/gestion/empleados/{employeeId}", Empleado("QA-CAT-E1", "Employee", "123-1111111-4", firstDepartmentId))).StatusCode);
        var updateEmployee = await Client.PutAsJsonAsync($"api/gestion/empleados/{employeeId}", Empleado("QA-CAT-E1U", "Employee Updated", "123-1111111-6", secondDepartmentId));
        Assert.Equal(HttpStatusCode.OK, updateEmployee.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/empleados", Empleado("QA-CAT-E3", "Employee Duplicate Updated Code", "123-1111111-6", secondDepartmentId))).StatusCode);

        object Vehicle(string plate, string ficha, long deptId, short? year = 2024, decimal? capacity = 20m, decimal odometer = 0m) => new { placa = plate, ficha, marca = "QA", modelo = "Test", anio = year, tipo = "Automóvil", departamentoId = deptId, capacidadTanqueGalones = capacity, odometroKm = odometer, activo = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-X", "QA-FX", 999999))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-X", "QA-FX", firstDepartmentId, 1899))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-X", "QA-FX", firstDepartmentId, capacity: 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-X", "QA-FX", firstDepartmentId, capacity: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-X", "QA-FX", firstDepartmentId, odometer: -1))).StatusCode);
        var vehResponse = await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-CAT-V1", "QA-CAT-F1", firstDepartmentId));
        Assert.Equal(HttpStatusCode.Created, vehResponse.StatusCode);
        var vehicleId = (await vehResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-CAT-V1", "QA-CAT-F2", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/gestion/vehiculos", Vehicle("QA-CAT-V2", "QA-CAT-F1", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/gestion/vehiculos/{vehicleId}", Vehicle("QA-CAT-V1U", "QA-CAT-F1U", 999999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/gestion/vehiculos/999999", Vehicle("QA-CAT-V1U", "QA-CAT-F1U", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/gestion/vehiculos/{vehicleId}", Vehicle("QA-CAT-V1U", "QA-CAT-F1U", secondDepartmentId, 2025, 25, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/gestion/vehiculos/{vehicleId}", Vehicle("QA-CAT-V1U", "QA-CAT-F1U", secondDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/vehiculos/{vehicleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/empleados/{employeeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/departamentos/{firstDepartmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/empleados", Empleado("QA-INACTIVE-D", "Inactive Department", "123-1111111-9", firstDepartmentId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync("api/gestion/desconocido/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync("api/gestion/empleados/999999")).StatusCode);
        var listedEmployees = await (await Client.GetAsync("api/catalogos/empleados")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(listedEmployees.EnumerateArray(), x => x.GetProperty("id").GetInt64() == employeeId);
        var listedVehicles = await (await Client.GetAsync("api/catalogos/vehiculos")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(listedVehicles.EnumerateArray(), x => x.GetProperty("id").GetInt64() == vehicleId);
        Client.DefaultRequestHeaders.Authorization = null;
        _ = admin;
    }

    [Fact]
    public async Task Solicitudes_validan_importes_activos_fechas_permisos_aprobacion_y_rechazo()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.admin.requests", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var catalog = await SeedCatalogAsync(stock: 20m);
        var requester = await LoginAsync("qa.requester", "SOLICITANTE");
        var supervisor = await LoginAsync("qa.supervisor", "SUPERVISOR");
        var expiry = DateTime.UtcNow.AddDays(2);
        object RequestBody(long employeeId, long vehicleId, decimal gallons, DateTime? expires = null) => new { empleadoId = employeeId, vehiculoId = vehicleId, departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId, cantidadSolicitadaGalones = gallons, fechaVencimiento = expires ?? expiry, tipoSolicitud = "MANUAL" };

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(999999, catalog.VehicleId, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, 999999, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 4, DateTime.UtcNow.AddDays(-1)))).StatusCode);
        var created = await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 10));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 5m, fechaVencimiento = expiry, usuarioAprobadorId = requester.Id })).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisor.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 0m, fechaVencimiento = expiry, usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 11m, fechaVencimiento = expiry, usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 5m, fechaVencimiento = DateTime.UtcNow.AddDays(-1), usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 5m, fechaVencimiento = expiry, usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 5m, fechaVencimiento = expiry, usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsync($"api/solicitudes/{requestId}/rechazar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/solicitudes/999999/aprobar", new { cantidadAutorizadaGalones = 1m, fechaVencimiento = expiry, usuarioAprobadorId = supervisor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync("api/solicitudes/999999/rechazar", null)).StatusCode);

        var rejected = await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 2));
        var rejectedId = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsync($"api/solicitudes/{rejectedId}/rechazar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsync($"api/solicitudes/{rejectedId}/rechazar", null)).StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        db.Empleados.Single(x => x.Id == catalog.EmployeeId).Activo = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 2))).StatusCode);
        db.Empleados.Single(x => x.Id == catalog.EmployeeId).Activo = true;
        db.Vehiculos.Single(x => x.Id == catalog.VehicleId).Activo = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/solicitudes", RequestBody(catalog.EmployeeId, catalog.VehicleId, 2))).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Theory]
    [InlineData("id_ticket")]
    [InlineData("qr_token")]
    [InlineData("id_empleado")]
    [InlineData("id_vehiculo")]
    [InlineData("id_departamento")]
    [InlineData("id_tipo_combustible")]
    [InlineData("cantidad_autorizada_galones")]
    [InlineData("fecha_creacion")]
    [InlineData("fecha_vencimiento")]
    public async Task QR_rechaza_cambios_individuales_en_cada_campo_firmado(string field)
    {
        await qa.ResetAsync();
        await LoginAsync("qa.admin.qr", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(includeAlternates: true);
        var issued = await IssueTicketAsync(catalog, 12m);
        Assert.True(await IsQrValidAsync(issued.Token));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var sql = field switch
            {
                "id_ticket" => $"UPDATE tickets SET id_ticket = '{Guid.NewGuid()}' WHERE id_ticket = '{issued.Id}'",
                "qr_token" => $"UPDATE tickets SET qr_token = 'modified-token-for-qa' WHERE id_ticket = '{issued.Id}'",
                "id_empleado" => $"UPDATE tickets SET id_empleado = {catalog.Employee2Id} WHERE id_ticket = '{issued.Id}'",
                "id_vehiculo" => $"UPDATE tickets SET id_vehiculo = {catalog.Vehicle2Id} WHERE id_ticket = '{issued.Id}'",
                "id_departamento" => $"UPDATE tickets SET id_departamento = {catalog.Department2Id} WHERE id_ticket = '{issued.Id}'",
                "id_tipo_combustible" => $"UPDATE tickets SET id_tipo_combustible = {catalog.Fuel2Id} WHERE id_ticket = '{issued.Id}'",
                "cantidad_autorizada_galones" => $"UPDATE tickets SET cantidad_autorizada_galones = 11 WHERE id_ticket = '{issued.Id}'",
                "fecha_creacion" => $"UPDATE tickets SET fecha_creacion = fecha_creacion - INTERVAL '1 day' WHERE id_ticket = '{issued.Id}'",
                "fecha_vencimiento" => $"UPDATE tickets SET fecha_vencimiento = fecha_vencimiento + INTERVAL '1 day' WHERE id_ticket = '{issued.Id}'",
                _ => throw new InvalidOperationException("Campo fuera de la lista segura del test.")
            };
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        var changedToken = field == "qr_token" ? "modified-token-for-qa" : issued.Token;
        Assert.False(await IsQrValidAsync(changedToken));
    }

    [Fact]
    public async Task QR_rechaza_token_inexistente_corrupto_firma_corrupta_vencido_y_consumido()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.admin.qr.states", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 8m);
        Assert.False(await IsQrValidAsync("not-an-existing-token"));
        Assert.False(await IsQrValidAsync("%%%corrupt%%%"));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET qr_hash = repeat('0',64) WHERE id_ticket = {issued.Id}");
        }
        Assert.False(await IsQrValidAsync(issued.Token));
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var deadline = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);
            var creation = TruncateToMicrosecond(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-2), DateTimeKind.Unspecified));
            deadline = TruncateToMicrosecond(deadline);
            var canonical = string.Join('|', "v2", issued.Id.ToString("D"), issued.RequestId.ToString(), catalog.EmployeeId.ToString(), catalog.VehicleId.ToString(), catalog.DepartmentId.ToString(), catalog.FuelId.ToString(), "8", (creation.Ticks / 10).ToString(), (deadline.Ticks / 10).ToString(), issued.Token);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ApiTestFactory.QrSecret));
            var signed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            var expiredTicket = await db.Tickets.SingleAsync(x => x.Id == issued.Id);
            expiredTicket.FechaCreacion = creation;
            expiredTicket.FechaVencimiento = deadline;
            expiredTicket.QrHash = signed;
            await db.SaveChangesAsync();
        }
        var expired = await ValidateQrAsync(issued.Token);
        Assert.False(expired.GetProperty("valido").GetBoolean());
        Assert.Equal("Vencido", expired.GetProperty("estado").GetString());
        var consumable = await IssueTicketAsync(catalog, 8m);
        Assert.True((await ValidateQrAsync(consumable.Token)).GetProperty("valido").GetBoolean());
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = consumable.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true });
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        var consumed = await ValidateQrAsync(consumable.Token);
        Assert.False(consumed.GetProperty("valido").GetBoolean());
        Assert.Equal("Consumido", consumed.GetProperty("estado").GetString());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Emision_concurrente_asigna_numeros_unicos_consecutivos_y_respeta_secuencia_configurada()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.admin.sequence", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var ids = new List<long>();
        for (var i = 0; i < 8; i++) ids.Add(await CreateApprovedRequestAsync(catalog, 5m));
        var responses = await Task.WhenAll(ids.Select(id => Client.PostAsJsonAsync("api/tickets", new { solicitudId = id })));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var tickets = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<JsonElement>()));
        var numbers = tickets.Select(x => x.GetProperty("numeroSecuencial").GetString()!).ToArray();
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        Assert.Equal(8, numbers.Select(x => int.Parse(x.Split('-')[2])).Distinct().Count());
        Assert.All(numbers, value => Assert.Matches(@"^COM-\d{4}-\d{6}$", value));
        var sequence = numbers.Select(x => int.Parse(x.Split('-')[2])).Order().ToArray();
        Assert.Equal(Enumerable.Range(sequence[0], 8), sequence);

        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE configuracion_tickets SET secuencia_actual = 40, anio_secuencia = EXTRACT(YEAR FROM CURRENT_DATE)::INTEGER");
        }
        var nextRequest = await CreateApprovedRequestAsync(catalog, 5m);
        var next = await (await Client.PostAsJsonAsync("api/tickets", new { solicitudId = nextRequest })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith("000041", next.GetProperty("numeroSecuencial").GetString());
    }

    [Fact]
    public async Task Despachos_simultaneos_con_stock_limitado_no_producen_stock_negativo_ni_lost_update()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.admin.stock-race", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 100m);
        var ticketA = await IssueTicketAsync(catalog, 60m);
        var ticketB = await IssueTicketAsync(catalog, 50m);
        var dispatcherA = await LoginAsync("qa.dispatch.stock.a", "DESPACHADOR");
        var dispatcherB = await LoginAsync("qa.dispatch.stock.b", "DESPACHADOR");
        using var clientA = qa.Factory.CreateClient();
        using var clientB = qa.Factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dispatcherA.Token);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dispatcherB.Token);
        Assert.True((await (await clientA.PostAsJsonAsync("api/tickets/validar", new { qrData = ticketA.Token })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        Assert.True((await (await clientB.PostAsJsonAsync("api/tickets/validar", new { qrData = ticketB.Token })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        var results = await Task.WhenAll(
            clientA.PostAsJsonAsync("api/despachos", new { ticketId = ticketA.Id.ToString(), galonesServidos = 60m, identidadConfirmada = true, tanqueId = catalog.TankId }),
            clientB.PostAsJsonAsync("api/despachos", new { ticketId = ticketB.Id.ToString(), galonesServidos = 50m, identidadConfirmada = true, tanqueId = catalog.TankId }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var stock = await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync();
        Assert.InRange(stock, 0m, 100m);
        Assert.Contains(stock, new[] { 40m, 50m });
        Assert.Equal(1, await db.Despachos.CountAsync());
        Assert.Equal(1, await db.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "SALIDA"));
        _ = admin;
    }

    [Fact]
    public async Task Ajustes_y_recepciones_rechazan_limites_y_preservan_movimientos()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.admin.inventory", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        object Adjustment(string kind, decimal amount) => new { tanqueId = catalog.TankId, tipo = kind, cantidadGalones = amount, motivo = "Test QA", usuarioId = admin.Id };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/inventario/ajustes", Adjustment("AJUSTE_POSITIVO", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/inventario/ajustes", Adjustment("OTRO", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("api/inventario/ajustes", Adjustment("AJUSTE_POSITIVO", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("api/inventario/ajustes", Adjustment("AJUSTE_NEGATIVO", 7))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/inventario/ajustes", Adjustment("MERMA", 999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = 999999, tipo = "AJUSTE_POSITIVO", cantidadGalones = 1m, motivo = "missing", usuarioId = admin.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/tanques", new { codigo = "QA-ZERO", estacionId = catalog.StationId, tipoCombustibleId = catalog.FuelId, capacidadGalones = 0m, existenciaActualGalones = 0m, nivelCriticoGalones = 0m, activo = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/gestion/tanques", new { codigo = "QA-CRIT", estacionId = catalog.StationId, tipoCombustibleId = catalog.FuelId, capacidadGalones = 50m, existenciaActualGalones = 0m, nivelCriticoGalones = 60m, activo = true })).StatusCode);
        var supplier = await Client.PostAsJsonAsync("api/recepciones/proveedores", new { nombre = "Proveedor QA Coverage", rnc = "QA-COVERAGE", activo = true });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        var supplierId = (await supplier.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        object Reception(decimal gallons, long tankId) => new { proveedorId = supplierId, numeroFactura = "QA-COVERAGE-1", fechaRecepcion = DateTime.UtcNow, usuarioReceptorId = admin.Id, detalles = new[] { new { tanqueId = tankId, volumenRecibidoGalones = gallons, costoUnitario = (decimal?)null } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/recepciones", Reception(0, catalog.TankId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/recepciones", Reception(-1, catalog.TankId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/recepciones", Reception(1, 999999))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/recepciones", Reception(90, catalog.TankId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/recepciones", new { proveedorId = supplierId, numeroFactura = "QA-empty", fechaRecepcion = DateTime.UtcNow, detalles = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/recepciones", new { proveedorId = 999999, numeroFactura = "QA-no-provider", fechaRecepcion = DateTime.UtcNow, detalles = new[] { new { tanqueId = catalog.TankId, volumenRecibidoGalones = 1m } } })).StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(15m, await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Assert.Equal(2, await db.MovimientosInventario.CountAsync());
        Assert.Equal(0, await db.Recepciones.CountAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task SignalR_publica_ajustes_mermas_y_solo_transiciones_reales_de_nivel_critico()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.inventory.live", "SUPERVISOR");
        var catalog = await SeedCatalogAsync(stock: 10m);
        var sink = qa.Factory.InventoryEvents;

        async Task Adjust(string type, decimal amount)
        {
            var response = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = type, cantidadGalones = amount, motivo = "Prueba realtime", usuarioId = actor.Id });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        await Adjust("AJUSTE_NEGATIVO", 6m); // 10 -> 4: NORMAL to CRITICO
        await Adjust("MERMA", 1m);          // 4 -> 3: remains CRITICO
        await Adjust("AJUSTE_POSITIVO", 4m); // 3 -> 7: CRITICO to NORMAL
        await Adjust("AJUSTE_POSITIVO", 1m); // 7 -> 8: remains NORMAL

        Assert.Equal(4, sink.Events.Count);
        Assert.Equal(new[] { "AJUSTE_NEGATIVO", "MERMA", "AJUSTE_POSITIVO", "AJUSTE_POSITIVO" }, sink.Events.Select(x => x.Updated.MovementType));
        Assert.Equal(new[] { (10m, 4m), (4m, 3m), (3m, 7m), (7m, 8m) }, sink.Events.Select(x => (x.Updated.PreviousQuantity, x.Updated.CurrentQuantity)));
        Assert.Equal(new bool?[] { true, null, false, null }, sink.Events.Select(x => (bool?)x.CriticalChange?.Critical));
        Assert.All(sink.Events, item =>
        {
            Assert.Equal(catalog.TankId, item.Updated.TankId);
            Assert.Equal(catalog.StationId, item.Updated.StationId);
            Assert.Equal(item.Updated.MovementId, item.Movement.MovementId);
            Assert.Equal(item.Updated.CurrentQuantity, item.Movement.CurrentQuantity);
            Assert.InRange(item.Updated.Percentage, 0m, 100m);
        });
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var stock = await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync();
        Assert.Equal(8m, stock);
        Assert.Equal(stock, sink.Events[^1].Updated.CurrentQuantity);
    }

    [Fact]
    public async Task SignalR_con_ajustes_concurrentes_conserva_snapshot_y_la_version_mas_nueva()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.inventory.live.concurrent", "SUPERVISOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var sink = qa.Factory.InventoryEvents;
        var first = Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 2m, motivo = "Concurrent A", usuarioId = actor.Id });
        var second = Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 3m, motivo = "Concurrent B", usuarioId = actor.Id });
        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(2, sink.Events.Count);
        Assert.All(sink.Events, item => Assert.Equal(item.Updated.CurrentQuantity, item.Movement.CurrentQuantity));
        Assert.Equal(25m, sink.Events.OrderBy(x => x.Updated.MovementId).Last().Updated.CurrentQuantity);
        Assert.Equal(25m, await TankStockAsync(catalog.TankId));
    }

    [Fact]
    public async Task SignalR_publica_recepcion_y_despacho_con_movimientos_persistidos_y_rechazos_no_publican()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.inventory.live.dispatch", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var supplier = new Proveedor { Nombre = "Realtime QA supplier", Activo = true };
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            db.Proveedores.Add(supplier);
            await db.SaveChangesAsync();
        }

        var receipt = await Client.PostAsJsonAsync("api/recepciones", new
        {
            proveedorId = supplier.Id, numeroFactura = "QA-REALTIME-01", fechaRecepcion = DateTime.UtcNow,
            usuarioReceptorId = actor.Id, detalles = new[] { new { tanqueId = catalog.TankId, volumenRecibidoGalones = 7m, costoUnitario = (decimal?)null } }
        });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        Assert.Single(qa.Factory.InventoryEvents.Events);
        Assert.Equal("ENTRADA", qa.Factory.InventoryEvents.Events[0].Movement.MovementType);
        Assert.Equal(20m, qa.Factory.InventoryEvents.Events[0].Updated.PreviousQuantity);
        Assert.Equal(27m, qa.Factory.InventoryEvents.Events[0].Updated.CurrentQuantity);

        var ticket = await IssueTicketAsync(catalog, 3m);
        Assert.True(await IsQrValidAsync(ticket.Token));
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = ticket.Id.ToString(), galonesServidos = 3m, identidadConfirmada = true, tanqueId = catalog.TankId });
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        var events = qa.Factory.InventoryEvents.Events;
        Assert.Equal(2, events.Count);
        Assert.Equal("SALIDA", events[1].Movement.MovementType);
        Assert.Equal(27m, events[1].Updated.PreviousQuantity);
        Assert.Equal(24m, events[1].Updated.CurrentQuantity);
        Assert.Equal("DESPACHO", events[1].Movement.ReferenceType);

        var duplicate = await Client.PostAsJsonAsync("api/despachos", new { ticketId = ticket.Id.ToString(), galonesServidos = 3m, identidadConfirmada = true, tanqueId = catalog.TankId });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(2, qa.Factory.InventoryEvents.Events.Count);
        await using var verify = qa.Factory.Services.CreateAsyncScope();
        var database = verify.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var persistedMovement = await database.MovimientosInventario.AsNoTracking().SingleAsync(x => x.Id == events[1].Updated.MovementId);
        Assert.Equal(persistedMovement.ReferenciaId, events[1].Movement.ReferenceId);
        Assert.Equal(24m, await database.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
    }

    [Fact]
    public async Task SignalR_no_publica_rollback_o_movimiento_rechazado_por_cierre_y_error_de_hub_no_revierte_commit()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.inventory.live.failures", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 10m);
        var sink = qa.Factory.InventoryEvents;

        sink.FailPublishes = true;
        var accepted = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 1m, motivo = "Sink unavailable", usuarioId = actor.Id });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(11m, await TankStockAsync(catalog.TankId));
        sink.Reset();

        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION qa_rechazar_auditoria_live() RETURNS trigger AS $$
                BEGIN IF NEW.accion = 'INVENTORY_ADJUSTED' THEN RAISE EXCEPTION 'QA rollback'; END IF; RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER qa_rechazar_auditoria_live BEFORE INSERT ON auditoria FOR EACH ROW EXECUTE FUNCTION qa_rechazar_auditoria_live();
                """);
        }
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_NEGATIVO", cantidadGalones = 2m, motivo = "Rollback", usuarioId = actor.Id }));
            Assert.Empty(sink.Events);
            Assert.Equal(11m, await TankStockAsync(catalog.TankId));
        }
        finally
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS qa_rechazar_auditoria_live ON auditoria; DROP FUNCTION IF EXISTS qa_rechazar_auditoria_live();");
        }

        var closed = await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, 11m)]);
        Assert.Equal(HttpStatusCode.Created, closed.StatusCode);
        sink.Reset();
        var rejected = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 1m, motivo = "Closed day", usuarioId = actor.Id });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Empty(sink.Events);
        Assert.Equal(11m, await TankStockAsync(catalog.TankId));
    }

    [Fact]
    public async Task Hub_de_inventario_exige_jwt_valido_en_negotiate()
    {
        await qa.ResetAsync();
        var anonymous = await Client.PostAsync("hubs/inventory/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var actor = await LoginAsync("qa.inventory.hub", "CONSULTA");
        Client.DefaultRequestHeaders.Authorization = null;
        var valid = await Client.PostAsync($"hubs/inventory/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(actor.Token)}", null);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var alteredParts = actor.Token.Split('.');
        alteredParts[2] = (alteredParts[2][0] == 'A' ? "B" : "A") + alteredParts[2][1..];
        var altered = string.Join('.', alteredParts);
        var invalid = await Client.PostAsync($"hubs/inventory/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(altered)}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task SignalR_real_recibe_movimiento_persistido_despues_de_operacion_de_inventario()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.inventory.realhub", "SUPERVISOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(qa.Factory.Server.BaseAddress, "/hubs/inventory"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(actor.Token);
                options.HttpMessageHandlerFactory = _ => qa.Factory.Server.CreateHandler();
            })
            .Build();
        var received = new TaskCompletionSource<InventoryUpdatedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<InventoryUpdatedEvent>("InventoryUpdated", message => received.TrySetResult(message));
        await connection.StartAsync();

        var response = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_NEGATIVO", cantidadGalones = 3m, motivo = "Hub integration", usuarioId = actor.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var eventPayload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(catalog.TankId, eventPayload.TankId);
        Assert.Equal(20m, eventPayload.PreviousQuantity);
        Assert.Equal(17m, eventPayload.CurrentQuantity);
        Assert.Equal("AJUSTE_NEGATIVO", eventPayload.MovementType);
        Assert.Equal(17m, await TankStockAsync(catalog.TankId));
        await connection.StopAsync();
    }

    private async Task<decimal> TankStockAsync(long tankId)
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>().Tanques
            .Where(x => x.Id == tankId).Select(x => x.ExistenciaActualGalones).SingleAsync();
    }

    [Fact]
    public async Task Auditoria_registra_eventos_filtra_accesos_sanitiza_secretos_y_bloquea_modificaciones()
    {
        await qa.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login", new { usuario = "desconocido", contrasena = ApiTestFactory.TestPassword })).StatusCode);
        var admin = await LoginAsync("qa.audit.admin", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/login/registro", new { usuario = "qa.audit.registered", correo = "qa.audit.registered@example.test", nombreCompleto = "Registered QA", contrasena = ApiTestFactory.TestPassword })).StatusCode);

        var roles = await GetRolesAsync();
        var departmentResponse = await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA-AUDIT-D", nombre = "QA Audit Department", activo = true });
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode);
        var auditDepartmentId = (await departmentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/gestion/departamentos/{auditDepartmentId}", new { codigo = "QA-AUDIT-D", nombre = "QA Audit Department Updated", activo = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/departamentos/{auditDepartmentId}")).StatusCode);
        var createdUser = await Client.PostAsJsonAsync("api/gestion/usuarios", Usuario("qa.audit.subject", "qa.audit.subject@example.test", "Audit Subject", ApiTestFactory.TestPassword, roles["CONSULTA"]));
        Assert.Equal(HttpStatusCode.Created, createdUser.StatusCode);
        var userId = (await createdUser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/gestion/usuarios/{userId}", new { correo = "qa.audit.subject@example.test", nombreCompleto = "Audit Subject Updated", rolId = roles["SUPERVISOR"] })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/usuarios/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync($"api/gestion/usuarios/{userId}/activar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync($"api/gestion/usuarios/{userId}/restablecer-contrasena", new { contrasena = "Audit-New-Password-2026" })).StatusCode);

        var catalog = await SeedCatalogAsync(stock: 0m);
        var toReject = await CreateApprovedRequestAsync(catalog, 2m);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsync($"api/solicitudes/{toReject}/rechazar", null)).StatusCode);
        var rejectedRequest = await Client.PostAsJsonAsync("api/solicitudes", new { empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId, departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId, cantidadSolicitadaGalones = 2m, fechaVencimiento = DateTime.UtcNow.AddDays(2), tipoSolicitud = "MANUAL" });
        Assert.Equal(HttpStatusCode.Created, rejectedRequest.StatusCode);
        var rejectedId = (await rejectedRequest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsync($"api/solicitudes/{rejectedId}/rechazar", null)).StatusCode);
        var issued = await IssueTicketAsync(catalog, 8m);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 15m, motivo = "Auditoría QA", usuarioId = admin.Id })).StatusCode);
        var supplier = await Client.PostAsJsonAsync("api/recepciones/proveedores", new { nombre = "QA Audit Provider", rnc = "QA-AUDIT-1", activo = true });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        var supplierId = (await supplier.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("api/recepciones", new { proveedorId = supplierId, numeroFactura = "QA-AUDIT-RECEIPT", fechaRecepcion = DateTime.UtcNow, detalles = new[] { new { tanqueId = catalog.TankId, volumenRecibidoGalones = 2m } } })).StatusCode);
        Assert.True((await (await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = issued.Token })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 3m, tanqueId = catalog.TankId, identidadConfirmada = true })).StatusCode);

        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IAuditoriaService>();
            await service.RegistrarAsync("SANITIZER_TEST", "TEST", "1", "EXITO", datosNuevos: new { contrasena = "do-not-store-password", qrToken = "do-not-store-token", privateSecret = "do-not-store-secret", visible = "safe" });
        }

        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var events = await db.Auditoria.AsNoTracking().ToListAsync();
            var actions = events.Select(x => x.Accion).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("LOGIN", actions);
            Assert.Contains(events, x => x.Accion == "LOGIN" && x.Resultado == "FALLO" && x.UsuarioId is null);
            Assert.Contains("USER_CREATED", actions);
            Assert.Contains("USER_REGISTERED", actions);
            Assert.Contains("USER_UPDATED", actions);
            Assert.Contains("USER_ROLE_CHANGED", actions);
            Assert.Contains("USER_DEACTIVATED", actions);
            Assert.Contains("USER_ACTIVATED", actions);
            Assert.Contains("USER_PASSWORD_RESET", actions);
            Assert.Contains("REQUEST_CREATED", actions);
            Assert.Contains("REQUEST_APPROVED", actions);
            Assert.Contains("TICKET_ISSUED", actions);
            Assert.Contains("QR_VALIDATED", actions);
            Assert.Contains("DISPATCH_RECORDED", actions);
            Assert.Contains("INVENTORY_ADJUSTED", actions);
            Assert.Contains("SUPPLIER_CREATED", actions);
            Assert.Contains("RECEIPT_CREATED", actions);
            Assert.Contains("REQUEST_REJECTED", actions);
            Assert.Contains("CATALOG_CREATED", actions);
            Assert.Contains("CATALOG_UPDATED", actions);
            Assert.Contains("CATALOG_DEACTIVATED", actions);
            Assert.All(events, audit => Assert.NotEqual(default, audit.FechaHora));
            Assert.Contains(events, x => x.Accion == "SANITIZER_TEST");
            Assert.Contains(events, x => x.DatosNuevos?.RootElement.GetRawText().Contains("visible", StringComparison.Ordinal) == true && !x.DatosNuevos.RootElement.GetRawText().Contains("do-not-store", StringComparison.Ordinal));
            var allJsonAndDetails = string.Join(" ", events.Select(x => $"{x.DatosAnteriores?.RootElement.GetRawText()} {x.DatosNuevos?.RootElement.GetRawText()} {x.Detalle}"));
            Assert.DoesNotContain("do-not-store-password", allJsonAndDetails, StringComparison.Ordinal);
            Assert.DoesNotContain("do-not-store-token", allJsonAndDetails, StringComparison.Ordinal);
            Assert.DoesNotContain("do-not-store-secret", allJsonAndDetails, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiTestFactory.TestPassword, allJsonAndDetails, StringComparison.Ordinal);
            var row = await db.Auditoria.OrderBy(x => x.Id).FirstAsync();
            var originalDetail = row.Detalle;
            row.Detalle = "tampered";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(row).State = EntityState.Detached;
            var deleteRow = await db.Auditoria.AsNoTracking().FirstAsync(x => x.Id == row.Id);
            db.Auditoria.Remove(deleteRow);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(deleteRow).State = EntityState.Detached;
            Assert.Equal(originalDetail, await db.Auditoria.AsNoTracking().Where(x => x.Id == row.Id).Select(x => x.Detalle).SingleAsync());
        }

        var auditor = await LoginAsync("qa.audit.reader", "AUDITOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auditor.Token);
        var filtered = await Client.GetAsync("api/auditoria?accion=USER_ROLE_CHANGED&resultado=EXITO");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var payload = await filtered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("total").GetInt32());
        Assert.Equal("USER_ROLE_CHANGED", payload.GetProperty("items")[0].GetProperty("accion").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("api/auditoria?resultado=UNKNOWN")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Client.PostAsJsonAsync("api/auditoria", new { accion = "TAMPER" })).StatusCode);

        var consulta = await LoginAsync("qa.audit.denied", "CONSULTA");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consulta.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/auditoria")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Anulacion_sin_JWT_responde_401()
    {
        await qa.ResetAsync();
        var response = await Client.PostAsJsonAsync($"api/tickets/{Guid.NewGuid()}/anular", new { motivo = "Duplicado" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("DESPACHADOR")]
    [InlineData("CONSULTA")]
    [InlineData("SOLICITANTE")]
    [InlineData("AUDITOR")]
    public async Task Anulacion_rechaza_roles_sin_permiso(string role)
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.denied.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var issued = await IssueTicketAsync(catalog, 5m);
        var user = await LoginAsync($"qa.ticket.cancel.denied.{role.ToLowerInvariant()}", role);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);

        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "No autorizado" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Theory]
    [InlineData("ADMINISTRADOR")]
    [InlineData("SUPERVISOR")]
    public async Task Anulacion_admite_administrador_y_supervisor(string role)
    {
        await qa.ResetAsync();
        await LoginAsync($"qa.ticket.cancel.allowed.admin.{role.ToLowerInvariant()}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        var issued = await IssueTicketAsync(catalog, 5m);
        var authorized = await LoginAsync($"qa.ticket.cancel.allowed.{role.ToLowerInvariant()}", role);

        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Emisión duplicada" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ANULADO", body.GetProperty("estado").GetString());
        Assert.Equal("Emisión duplicada", body.GetProperty("motivoAnulacion").GetString());
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var ticket = await db.Tickets.SingleAsync(x => x.Id == issued.Id);
        Assert.Equal(EstadoTicket.ANULADO, ticket.Estado);
        Assert.Equal(authorized.Id, ticket.UsuarioAnulacionId);
        Assert.NotNull(ticket.AnuladoEn);
        Assert.Equal("Emisión duplicada", ticket.MotivoAnulacion);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Anulacion_rechaza_motivo_nulo_vacio_o_espacios(string? motivo)
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.reason.empty", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anulacion_rechaza_motivo_mayor_a_500_caracteres()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.reason.long", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = new string('x', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anulacion_de_ticket_inexistente_responde_404()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.missing", "ADMINISTRADOR");
        var response = await Client.PostAsJsonAsync($"api/tickets/{Guid.NewGuid()}/anular", new { motivo = "Ticket inexistente" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anulacion_persiste_estado_actor_fecha_motivo_y_evento_de_auditoria_sin_QR()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.ticket.cancel.audit", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Solicitud revocada" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var ticket = await db.Tickets.SingleAsync(x => x.Id == issued.Id);
        var audit = await db.Auditoria.SingleAsync(x => x.Accion == "TICKET_ANULADO" && x.EntidadId == issued.Id.ToString("D"));
        Assert.Equal(EstadoTicket.ANULADO, ticket.Estado);
        Assert.Equal(admin.Id, ticket.UsuarioAnulacionId);
        Assert.NotNull(ticket.AnuladoEn);
        Assert.Equal("Solicitud revocada", ticket.MotivoAnulacion);
        Assert.Equal(admin.Id, audit.UsuarioId);
        Assert.Equal("EXITO", audit.Resultado);
        Assert.Contains("CREADO", audit.DatosAnteriores!.RootElement.GetRawText());
        Assert.Contains("ANULADO", audit.DatosNuevos!.RootElement.GetRawText());
        Assert.Contains("Solicitud revocada", audit.DatosNuevos.RootElement.GetRawText());
        var snapshots = audit.DatosAnteriores.RootElement.GetRawText() + audit.DatosNuevos.RootElement.GetRawText() + audit.Detalle;
        Assert.DoesNotContain(issued.Token, snapshots, StringComparison.Ordinal);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Doble_anulacion_responde_conflicto_y_audita_una_sola_vez()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.twice", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Primera anulación" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Segunda anulación" })).StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(1, await db.Auditoria.CountAsync(x => x.Accion == "TICKET_ANULADO"));
        Assert.Equal("Primera anulación", await db.Tickets.Where(x => x.Id == issued.Id).Select(x => x.MotivoAnulacion).SingleAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Ticket_consumido_no_se_puede_anular()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.consumed", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 2m, tanqueId = catalog.TankId, identidadConfirmada = true })).StatusCode);

        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Intento posterior al despacho" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(EstadoTicket.CONSUMIDO, await db.Tickets.Where(x => x.Id == issued.Id).Select(x => x.Estado).SingleAsync());
        Assert.Equal(0, await db.Auditoria.CountAsync(x => x.Accion == "TICKET_ANULADO"));
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Ticket_proximo_a_vencer_es_derivado_sin_persistirlo()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.status.near", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        DateTime expiry;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var expiryDb = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            expiry = await expiryDb.Tickets.Where(x => x.Id == issued.Id).Select(x => x.FechaVencimiento).SingleAsync();
        }
        var effectiveNow = DateTime.SpecifyKind(expiry.Subtract(TicketLifecycleService.ProximoAVencerThreshold), DateTimeKind.Utc);
        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(effectiveNow));

        var response = await Client.GetAsync($"api/tickets/{issued.Id}");
        Assert.Equal("PROXIMO_A_VENCER", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString());
        await using var verification = qa.Factory.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(EstadoTicket.CREADO, await verificationDb.Tickets.Where(x => x.Id == issued.Id).Select(x => x.Estado).SingleAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Ticket_vencido_al_limite_no_valida_ni_se_despacha_y_no_altera_inventario()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.status.expired", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        DateTime expiry;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var expiryDb = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            expiry = await expiryDb.Tickets.Where(x => x.Id == issued.Id).Select(x => x.FechaVencimiento).SingleAsync();
        }
        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc)));

        var listed = await (await Client.GetAsync($"api/tickets/{issued.Id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VENCIDO", listed.GetProperty("estado").GetString());
        var qr = await ValidateQrAsync(issued.Token);
        Assert.False(qr.GetProperty("valido").GetBoolean());
        Assert.Equal("Vencido", qr.GetProperty("estado").GetString());
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true });
        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);

        await using var verification = qa.Factory.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(20m, await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Assert.Equal(0, await db.Despachos.CountAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Ticket_vencido_puede_anularse_con_motivo_y_se_vuelve_terminal()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.expired", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        DateTime expiry;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            expiry = await db.Tickets.Where(x => x.Id == issued.Id).Select(x => x.FechaVencimiento).SingleAsync();
        }
        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc)));

        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Vencido, cierre administrativo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await (await Client.GetAsync($"api/tickets/{issued.Id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ANULADO", state.GetProperty("estado").GetString());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task QR_y_despacho_rechazan_ticket_anulado_sin_afectar_stock()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.ticket.cancel.qr", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Datos incorrectos" })).StatusCode);

        var qr = await ValidateQrAsync(issued.Token);
        Assert.False(qr.GetProperty("valido").GetBoolean());
        Assert.Equal("Anulado", qr.GetProperty("estado").GetString());
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true });
        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);

        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(20m, await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Assert.Equal(0, await db.Despachos.CountAsync());
        Assert.Equal(0, await db.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "SALIDA"));
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Concurrencia_anulacion_vs_despacho_solo_persiste_una_transicion_terminal()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.ticket.race.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        var dispatcher = await LoginAsync("qa.ticket.race.dispatcher", "DESPACHADOR");
        using var cancelClient = AuthenticatedClient(admin.Token);
        using var dispatchClient = AuthenticatedClient(dispatcher.Token);
        var validation = await dispatchClient.PostAsJsonAsync("api/tickets/validar", new { qrData = issued.Token });
        Assert.True((await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());

        var results = await Task.WhenAll(
            cancelClient.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Carrera QA" }),
            dispatchClient.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 2m, tanqueId = catalog.TankId, identidadConfirmada = true }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);

        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var state = await db.Tickets.Where(x => x.Id == issued.Id).Select(x => x.Estado).SingleAsync();
        var dispatchCount = await db.Despachos.CountAsync(x => x.TicketId == issued.Id);
        var stock = await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync();
        Assert.Contains(state, new[] { EstadoTicket.ANULADO, EstadoTicket.CONSUMIDO });
        Assert.Equal(state == EstadoTicket.CONSUMIDO ? 1 : 0, dispatchCount);
        Assert.Equal(state == EstadoTicket.CONSUMIDO ? 18m : 20m, stock);
        Assert.Equal(state == EstadoTicket.ANULADO ? 1 : 0, await db.Auditoria.CountAsync(x => x.Accion == "TICKET_ANULADO"));
    }

    [Fact]
    public async Task Concurrencia_dos_anulaciones_genera_un_exito_un_conflicto_y_un_evento()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.ticket.race.cancel.admin", "ADMINISTRADOR");
        var issued = await IssueTicketAsync(await SeedCatalogAsync(), 5m);
        var supervisor = await LoginAsync("qa.ticket.race.cancel.supervisor", "SUPERVISOR");
        using var adminClient = AuthenticatedClient(admin.Token);
        using var supervisorClient = AuthenticatedClient(supervisor.Token);

        var results = await Task.WhenAll(
            adminClient.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Motivo admin" }),
            supervisorClient.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "Motivo supervisor" }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);

        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(EstadoTicket.ANULADO, await db.Tickets.Where(x => x.Id == issued.Id).Select(x => x.Estado).SingleAsync());
        Assert.Equal(1, await db.Auditoria.CountAsync(x => x.Accion == "TICKET_ANULADO"));
    }

    [Fact]
    public async Task Base_de_datos_rechaza_transiciones_invalidas_y_terminales()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.ticket.transition.guard", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var toSent = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET estado = 'ENVIADO' WHERE id_ticket = {issued.Id}"));
        Assert.Contains("Transición inválida", toSent.MessageText, StringComparison.Ordinal);
        var withoutDispatch = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET estado = 'CONSUMIDO', consumido_en = NOW() WHERE id_ticket = {issued.Id}"));
        Assert.Contains("Transición inválida", withoutDispatch.MessageText, StringComparison.Ordinal);
        Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true })).StatusCode);
        var consumedToVoid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET estado = 'ANULADO', anulado_en = NOW(), motivo_anulacion = 'No permitido', id_usuario_anulacion = {admin.Id} WHERE id_ticket = {issued.Id}"));
        Assert.Contains("Transición inválida", consumedToVoid.MessageText, StringComparison.Ordinal);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Envio_email_entrega_qr_firmado_persiste_estado_historial_y_auditoria_y_es_idempotente()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.delivery.email", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "luis.mora@example.test", "8095550100");
        var issued = await IssueTicketAsync(catalog, 8m);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal("CREADO", (await db.Tickets.SingleAsync(x => x.Id == issued.Id)).Estado.ToString());
        }

        var key = Guid.NewGuid();
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ENVIADO", payload.GetProperty("estadoTicket").GetString());
        var email = Assert.Single(qa.Factory.EmailFake.Sent);
        Assert.Equal("luis.mora@example.test", email.To);
        Assert.Contains("COM-2026-000001", email.Subject);
        Assert.Contains("QA Employee One", email.TextBody);
        Assert.Contains("QA-COV-1", email.TextBody);
        Assert.Contains("QA Coverage Department", email.TextBody);
        Assert.Contains("Combustible:", email.TextBody);
        Assert.Contains("8.00 gal", email.TextBody);
        Assert.Contains("Vence:", email.TextBody);
        Assert.Contains("cid:ticket-qr", email.HtmlBody);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, email.QrPng[..4]);
        Assert.DoesNotContain(ApiTestFactory.QrSecret, email.TextBody, StringComparison.Ordinal);

        using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal("ENVIADO", (await db.Tickets.SingleAsync(x => x.Id == issued.Id)).Estado.ToString());
            var saved = await db.EnviosTicket.SingleAsync(x => x.TicketId == issued.Id);
            Assert.Equal("CORREO", saved.Canal);
            Assert.Equal("ENVIADO", saved.EstadoEnvio);
            Assert.Equal(1, saved.Intento);
            Assert.Equal("FAKE-EMAIL", saved.Proveedor);
            Assert.NotEqual(Guid.Empty, saved.LoteId);
            Assert.Equal(key, saved.IdempotencyKey);
            Assert.Null(saved.DetalleError);
            Assert.Contains(await db.Auditoria.Where(x => x.EntidadId == "COM-2026-000001").Select(x => x.Accion).ToListAsync(), x => x == "TICKET_ENVIO_SOLICITADO");
            Assert.Equal(1, await db.Auditoria.CountAsync(x => x.Accion == "TICKET_ENVIADO"));
            Assert.Equal(admin.Id, await db.Auditoria.Where(x => x.Accion == "TICKET_ENVIO_SOLICITADO").Select(x => x.UsuarioId).SingleAsync());
        }

        var history = await Client.GetAsync($"api/tickets/{issued.Id}/envios");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyJson = await history.Content.ReadAsStringAsync();
        Assert.Contains("l***@example.test", historyJson);
        Assert.DoesNotContain("luis.mora@example.test", historyJson, StringComparison.Ordinal);
        var mismatchedReplay = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Conflict, mismatchedReplay.StatusCode);
        Assert.Empty(qa.Factory.SmsFake.Sent);
        var replay = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = key });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicadoIdempotente").GetBoolean());
        Assert.Single(qa.Factory.EmailFake.Sent);
        Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
    }

    [Fact]
    public async Task Envio_sms_usa_url_https_con_token_qr_existente_y_no_hace_request_externo_en_test()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.sms", "SUPERVISOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "luis.mora@example.test", "+18095550101");
        var issued = await IssueTicketAsync(catalog, 6m);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sms = Assert.Single(qa.Factory.SmsFake.Sent);
        Assert.Equal("+18095550101", sms.To);
        Assert.Contains("COM-2026-000001", sms.Body);
        Assert.Contains("6 gal", sms.Body);
        Assert.Contains("https://tickets.qa.example.test/api/tickets/public/qr?token=", sms.Body);
        Assert.Contains(Uri.EscapeDataString(issued.Token), sms.Body);
        Assert.DoesNotContain(ApiTestFactory.QrSecret, sms.Body, StringComparison.Ordinal);
        var qrUrl = sms.Body[(sms.Body.IndexOf("https://", StringComparison.Ordinal))..].Split(' ')[0];
        var publicQr = await Client.GetAsync(qrUrl.Replace("https://tickets.qa.example.test", ""));
        Assert.Equal(HttpStatusCode.OK, publicQr.StatusCode);
        Assert.Equal("image/png", publicQr.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ENVIADO", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estadoTicket").GetString());
    }

    [Fact]
    public async Task Envio_ambos_con_un_fallo_mantiene_pendiente_y_reintenta_solo_el_canal_fallido()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.partial", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "luis.mora@example.test", "8095550102");
        var issued = await IssueTicketAsync(catalog, 7m);
        qa.Factory.SmsFake.FailNext("El proveedor SMS informó un error temporal.");
        var initial = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "AMBOS", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var initialBody = await initial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDIENTE", initialBody.GetProperty("estadoTicket").GetString());
        Assert.Equal("ENVIADO", initialBody.GetProperty("envios")[0].GetProperty("estado").GetString());
        Assert.Equal("FALLIDO", initialBody.GetProperty("envios")[1].GetProperty("estado").GetString());
        Assert.Single(qa.Factory.EmailFake.Sent);
        Assert.Single(qa.Factory.SmsFake.Sent);

        var retryKey = Guid.NewGuid();
        var retry = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/reenviar", new { canal = "SMS", idempotencyKey = retryKey });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("ENVIADO", (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estadoTicket").GetString());
        Assert.Single(qa.Factory.EmailFake.Sent);
        Assert.Equal(2, qa.Factory.SmsFake.Sent.Count);
        using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var attempts = await db.EnviosTicket.Where(x => x.TicketId == issued.Id).OrderBy(x => x.Canal).ThenBy(x => x.Intento).ToListAsync();
        Assert.Equal(3, attempts.Count);
        Assert.Equal(new[] { "ENVIADO", "FALLIDO", "ENVIADO" }, attempts.Select(x => x.EstadoEnvio).ToArray());
        Assert.Equal(new[] { 1, 1, 2 }, attempts.Select(x => x.Intento).ToArray());
        Assert.DoesNotContain("ApiKey", string.Join(" ", attempts.Select(x => x.DetalleError)), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EMAIL", "SMTP authentication failed; password=private-test-secret")]
    [InlineData("EMAIL", "Tiempo de espera agotado al conectar con SMTP.")]
    [InlineData("SMS", "El proveedor SMS informó un error temporal.")]
    [InlineData("SMS", "Tiempo de espera agotado en el proveedor SMS.")]
    public async Task Fallos_de_proveedor_se_persisten_sanitizados_sin_marcar_enviado(string channel, string safeFailure)
    {
        await qa.ResetAsync();
        await LoginAsync($"qa.delivery.failure.{channel}.{Guid.NewGuid():N}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "user@example.test", "+18095550103");
        var issued = await IssueTicketAsync(catalog, 3m);
        var uncertain = safeFailure.StartsWith("Tiempo de espera", StringComparison.Ordinal);
        if (channel == "EMAIL") qa.Factory.EmailFake.FailNext(safeFailure, uncertain); else qa.Factory.SmsFake.FailNext(safeFailure, uncertain);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = channel, idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDIENTE", body.GetProperty("estadoTicket").GetString());
        Assert.Equal(uncertain ? "PENDIENTE" : "FALLIDO", body.GetProperty("envios")[0].GetProperty("estado").GetString());
        Assert.DoesNotContain("private-test-secret", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal("PENDIENTE", (await db.Tickets.SingleAsync(x => x.Id == issued.Id)).Estado.ToString());
        var storedError = await db.EnviosTicket.Where(x => x.TicketId == issued.Id).Select(x => x.DetalleError).SingleAsync();
        if (safeFailure.Contains("private-test-secret", StringComparison.Ordinal)) Assert.Contains("[redactado]", storedError);
        else Assert.Equal(safeFailure, storedError);
        Assert.Equal(1, await db.Auditoria.CountAsync(x => x.Accion == (uncertain ? "TICKET_ENVIO_INCIERTO" : "TICKET_ENVIO_FALLIDO")));
        Assert.Equal(uncertain ? 0 : 1, await db.Notificaciones.CountAsync(x => x.Tipo == "FALLO_INTEGRACION"));
    }

    [Fact]
    public async Task Timeout_deja_resultado_pendiente_y_un_replay_o_reintento_no_duplica_el_envio()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.timeout-idempotency", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "timeout@example.test", "8095550108");
        var issued = await IssueTicketAsync(catalog, 3m);
        qa.Factory.SmsFake.FailNext("Tiempo de espera agotado en el proveedor SMS; verifica el proveedor antes de reintentar.", outcomeUncertain: true);
        var originalKey = Guid.NewGuid();
        var first = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = originalKey });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("PENDIENTE", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("envios")[0].GetProperty("estado").GetString());
        var explicitRetry = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/reenviar", new { canal = "SMS", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, explicitRetry.StatusCode);
        var replay = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = originalKey });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicadoIdempotente").GetBoolean());
        Assert.Single(qa.Factory.SmsFake.Sent);
    }

    [Fact]
    public async Task Envio_incierto_se_puede_reconciliar_tras_espera_y_actualiza_estado_y_auditoria()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.reconcile.sent", "SUPERVISOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "reconcile@example.test", "+18095550109");
        var issued = await IssueTicketAsync(catalog, 3m);
        qa.Factory.SmsFake.FailNext("Tiempo de espera agotado; resultado incierto.", outcomeUncertain: true);
        var response = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await Client.GetFromJsonAsync<JsonElement[]>($"api/tickets/{issued.Id}/envios");
        var envioId = history!.Single().GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/{envioId}/reconciliar", new { estado = "ENVIADO" })).StatusCode);
        qa.Factory.Clock.SetUtcNow(qa.Factory.Clock.GetUtcNow().AddMinutes(6));
        var reconciled = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/{envioId}/reconciliar", new { estado = "ENVIADO" });
        Assert.Equal(HttpStatusCode.OK, reconciled.StatusCode);
        var outcome = await reconciled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ENVIADO", outcome.GetProperty("estadoTicket").GetString());
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal("ENVIADO", (await db.EnviosTicket.SingleAsync(x => x.Id == envioId)).EstadoEnvio);
        Assert.Contains(await db.Auditoria.Select(x => x.Accion).ToListAsync(), action => action == "TICKET_ENVIO_RECONCILIADO");
    }

    [Fact]
    public async Task Reconciliar_fallido_habilita_reintento_del_canal_y_rechaza_estados_no_admitidos()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.reconcile.failed", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "reconcile-failed@example.test", "+18095550110");
        var issued = await IssueTicketAsync(catalog, 3m);
        qa.Factory.EmailFake.FailNext("Tiempo de espera agotado; resultado incierto.", outcomeUncertain: true);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() })).StatusCode);
        var envioId = (await Client.GetFromJsonAsync<JsonElement[]>($"api/tickets/{issued.Id}/envios"))!.Single().GetProperty("id").GetInt64();
        qa.Factory.Clock.SetUtcNow(qa.Factory.Clock.GetUtcNow().AddMinutes(6));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/{envioId}/reconciliar", new { estado = "QUIZA" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/{envioId}/reconciliar", new { estado = "FALLIDO" })).StatusCode);
        var retried = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/reenviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("ENVIADO", (await retried.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estadoTicket").GetString());
        Assert.Equal(2, qa.Factory.EmailFake.Sent.Count);
    }

    [Fact]
    public async Task Doble_solicitud_concurrente_del_mismo_ticket_y_canal_solo_llama_al_proveedor_una_vez()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.delivery.race.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "race@example.test", "8095550104");
        var issued = await IssueTicketAsync(catalog, 3m);
        var supervisor = await LoginAsync("qa.delivery.race.supervisor", "SUPERVISOR");
        using var firstClient = AuthenticatedClient(admin.Token);
        using var secondClient = AuthenticatedClient(supervisor.Token);
        var enteredProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        qa.Factory.EmailFake.GateNext(async cancellation => { enteredProvider.SetResult(); await releaseProvider.Task.WaitAsync(cancellation); });
        var first = firstClient.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() });
        await enteredProvider.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await secondClient.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        releaseProvider.SetResult();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Single(qa.Factory.EmailFake.Sent);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(1, await db.EnviosTicket.CountAsync(x => x.TicketId == issued.Id));
    }

    [Fact]
    public async Task Envio_exige_autenticacion_y_rol_administrativo_o_supervisor()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.delivery.rbac.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "rbac@example.test", "8095550105");
        var issued = await IssueTicketAsync(catalog, 3m);
        using var anonymous = qa.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/1/reconciliar", new { estado = "ENVIADO" })).StatusCode);
        var reader = await LoginAsync("qa.delivery.rbac.reader", "CONSULTA");
        using var readerClient = AuthenticatedClient(reader.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.PostAsJsonAsync($"api/tickets/{issued.Id}/envios/1/reconciliar", new { estado = "ENVIADO" })).StatusCode);
        using var authorized = AuthenticatedClient(admin.Token);
        Assert.Equal(HttpStatusCode.OK, (await authorized.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() })).StatusCode);
    }

    [Theory]
    [InlineData("ANULADO")]
    [InlineData("CONSUMIDO")]
    [InlineData("VENCIDO")]
    public async Task No_se_envian_tickets_anulados_consumidos_o_vencidos(string terminalState)
    {
        await qa.ResetAsync();
        var admin = await LoginAsync($"qa.delivery.blocked.{terminalState}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        await SetEmployeeContactAsync(catalog.EmployeeId, "blocked@example.test", "8095550106");
        var issued = await IssueTicketAsync(catalog, 3m);
        if (terminalState == "ANULADO")
            Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/anular", new { motivo = "QA policy" })).StatusCode);
        else if (terminalState == "CONSUMIDO")
        {
            Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/despachos", new { ticketId = issued.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true })).StatusCode);
        }
        else qa.Factory.Clock.SetUtcNow(DateTimeOffset.Parse("2035-01-01T00:00:00Z"));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() })).StatusCode);
        Assert.Empty(qa.Factory.EmailFake.Sent);
        using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Empty(await db.EnviosTicket.Where(x => x.TicketId == issued.Id).ToListAsync());
        _ = admin;
    }

    [Fact]
    public async Task Destinatarios_invalidos_y_canales_desconocidos_no_salen_a_proveedor_y_quedan_registrados()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.invalid", "SUPERVISOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "correo inválido", "123");
        var issued = await IssueTicketAsync(catalog, 3m);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "FAX", idempotencyKey = Guid.NewGuid() })).StatusCode);
        var emailResult = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, emailResult.StatusCode);
        var smsResult = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "SMS", idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, smsResult.StatusCode);
        Assert.Empty(qa.Factory.EmailFake.Sent);
        Assert.Empty(qa.Factory.SmsFake.Sent);
        using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(new[] { "FALLIDO", "FALLIDO" }, await db.EnviosTicket.Where(x => x.TicketId == issued.Id).OrderBy(x => x.Canal).Select(x => x.EstadoEnvio).ToArrayAsync());
    }

    [Fact]
    public async Task Fallo_de_auditoria_despues_de_aceptacion_del_proveedor_deja_intento_pendiente_sin_reenvio_automatico()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.delivery.audit-failure", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync();
        await SetEmployeeContactAsync(catalog.EmployeeId, "audit@example.test", "8095550107");
        var issued = await IssueTicketAsync(catalog, 3m);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION qa_reject_delivery_audit() RETURNS TRIGGER AS $$
                BEGIN IF NEW.accion = 'TICKET_ENVIADO' THEN RAISE EXCEPTION 'QA audit failure'; END IF; RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER qa_reject_delivery_audit BEFORE INSERT ON auditoria FOR EACH ROW EXECUTE FUNCTION qa_reject_delivery_audit();
                """);
        }
        try
        {
            var key = Guid.NewGuid();
            await Assert.ThrowsAsync<DbUpdateException>(() => Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = key }));
            Assert.Single(qa.Factory.EmailFake.Sent);
            var replay = await Client.PostAsJsonAsync($"api/tickets/{issued.Id}/enviar", new { canal = "EMAIL", idempotencyKey = key });
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicadoIdempotente").GetBoolean());
            Assert.Single(qa.Factory.EmailFake.Sent);
            using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal("PENDIENTE", (await db.Tickets.SingleAsync(x => x.Id == issued.Id)).Estado.ToString());
            Assert.Equal("PENDIENTE", (await db.EnviosTicket.SingleAsync(x => x.TicketId == issued.Id)).EstadoEnvio);
        }
        finally
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS qa_reject_delivery_audit ON auditoria; DROP FUNCTION IF EXISTS qa_reject_delivery_audit();");
        }
    }

    [Fact]
    public async Task Cierre_diario_sin_movimientos_persiste_auditoria_impide_edicion_y_genera_acta_pdf()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.close.empty", "DESPACHADOR");
        var catalog = await SeedCatalogAsync(stock: 40m);
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var preview = await Client.GetAsync($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, previewBody.GetProperty("resumen").GetProperty("inventarioInicialGalones").GetDecimal());
        Assert.Equal(40m, previewBody.GetProperty("resumen").GetProperty("inventarioTeoricoFinalGalones").GetDecimal());
        Assert.Equal(0, previewBody.GetProperty("resumen").GetProperty("cantidadDespachos").GetInt32());

        var created = await PostCloseAsync(Client, catalog.StationId, day, [(catalog.TankId, 40m)]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var closeId = body.GetProperty("id").GetInt64();
        Assert.Equal("CERRADO", body.GetProperty("estado").GetString());
        Assert.Equal(0m, body.GetProperty("diferenciaGalones").GetDecimal());
        Assert.Equal($"/api/cierres-diarios/{closeId}/pdf", body.GetProperty("rutaActaPdf").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await PostCloseAsync(Client, catalog.StationId, day, [(catalog.TankId, 40m)])).StatusCode);

        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var saved = await db.CierresDiarios.SingleAsync(x => x.Id == closeId);
            Assert.Equal(actor.Id, saved.UsuarioCierreId);
            Assert.Equal(40m, saved.InventarioFisicoGalones);
            Assert.Equal(0m, saved.DiferenciaGalones);
            Assert.Equal(1, await db.Auditoria.CountAsync(x => x.Accion == "CIERRE_DIARIO_CREADO" && x.EntidadId == closeId.ToString()));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE cierres_diarios SET observaciones = 'alterado' WHERE id_cierre = {0}", closeId));
        }

        var summaryAfter = await Client.GetAsync($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={day:yyyy-MM-dd}");
        var afterJson = await summaryAfter.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(afterJson.GetProperty("cerrado").GetBoolean());
        Assert.Equal(40m, afterJson.GetProperty("cierre").GetProperty("inventarioFisicoGalones").GetDecimal());
        await LoginAsync("qa.close.afterclose.admin", "ADMINISTRADOR");
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 1m, motivo = "Tras cierre", usuarioId = actor.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Client.PutAsJsonAsync($"api/cierres-diarios/{closeId}", new { inventarioFinalGalones = 1m })).StatusCode);

        var pdfResponse = await Client.GetAsync($"api/cierres-diarios/{closeId}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        var pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.True(pdf.Length > 500);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8));
        var pdfText = Encoding.ASCII.GetString(pdf);
        Assert.Contains("CIERRE DIARIO DE COMBUSTIBLE", pdfText);
        Assert.Contains($"Cierre: {closeId}", pdfText);
        Assert.Contains($"Fecha operacional UTC: {day:yyyy-MM-dd}", pdfText);
        Assert.Contains("Inventario fisico final: 40.00 gal", pdfText);
        Assert.Contains("Responsable: QA Coverage User", pdfText);
        Assert.Contains("QA Coverage Station", pdfText);
        Assert.Contains("Inventario inicial: 40.00 gal", pdfText);
        Assert.Contains("Entradas del dia: 0.00 gal", pdfText);
        Assert.Contains("Despachos/salidas por tickets: 0.00 gal", pdfText);
        Assert.Contains("Ajustes netos \\(+/-\\): 0.00 gal", pdfText);
        Assert.Contains("Inventario teorico final: 40.00 gal", pdfText);
        Assert.Contains("Diferencia \\(fisico - teorico\\): 0.00 gal", pdfText);
        Assert.Contains("Cantidad de despachos: 0", pdfText);
        Assert.Contains("Generado en UTC:", pdfText);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("api/cierres-diarios/999999/pdf")).StatusCode);
    }

    [Fact]
    public async Task Cierre_calcula_recepcion_ajustes_y_merma_desde_movimientos_persistidos()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.close.adjustments", "SUPERVISOR");
        var catalog = await SeedCatalogAsync(stock: 50m);
        long supplierId;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            db.Proveedores.Add(new Proveedor { Nombre = "QA Supplier", Activo = true });
            await db.SaveChangesAsync();
            supplierId = await db.Proveedores.Select(x => x.Id).SingleAsync();
        }
        foreach (var (kind, gallons) in new[] { ("AJUSTE_POSITIVO", 10m), ("AJUSTE_NEGATIVO", 5m), ("MERMA", 2m) })
        {
            var response = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = kind, cantidadGalones = gallons, motivo = "Ajuste de control", usuarioId = actor.Id });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var receipt = await Client.PostAsJsonAsync("api/recepciones", new
        {
            proveedorId = supplierId, numeroFactura = "QA-RF18-01", fechaRecepcion = DateTime.UtcNow,
            usuarioReceptorId = actor.Id, observaciones = "Entrada diaria",
            detalles = new[] { new { tanqueId = catalog.TankId, volumenRecibidoGalones = 7m, costoUnitario = (decimal?)null } }
        });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var preview = await Client.GetFromJsonAsync<JsonElement>($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={day:yyyy-MM-dd}");
        var summary = preview.GetProperty("resumen");
        Assert.Equal(7m, summary.GetProperty("volumenRecibidoGalones").GetDecimal());
        Assert.Equal(5m, summary.GetProperty("ajustesGalones").GetDecimal());
        Assert.Equal(2m, summary.GetProperty("mermasGalones").GetDecimal());
        Assert.Equal(60m, summary.GetProperty("inventarioTeoricoFinalGalones").GetDecimal());

        var created = await PostCloseAsync(Client, catalog.StationId, day, [(catalog.TankId, 62m)]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var cierre = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(60m, cierre.GetProperty("inventarioFinalGalones").GetDecimal());
        Assert.Equal(62m, cierre.GetProperty("inventarioFisicoGalones").GetDecimal());
        Assert.Equal(2m, cierre.GetProperty("diferenciaGalones").GetDecimal());
        Assert.Equal(0, cierre.GetProperty("cantidadDespachos").GetInt32());
    }

    [Theory]
    [InlineData(38, -2)]
    [InlineData(40, 0)]
    [InlineData(42, 2)]
    public async Task Cierre_guarda_diferencia_fisica_menos_teorica(decimal physical, decimal difference)
    {
        await qa.ResetAsync();
        await LoginAsync($"qa.close.diff.{physical}", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 40m);
        var response = await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, physical)]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var closure = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(difference, closure.GetProperty("diferenciaGalones").GetDecimal());
    }

    [Theory]
    [InlineData("DESPACHADOR")]
    [InlineData("SUPERVISOR")]
    [InlineData("ADMINISTRADOR")]
    public async Task Cierre_lo_pueden_crear_los_roles_operativos_autorizados(string role)
    {
        await qa.ResetAsync();
        await LoginAsync($"qa.close.allow.{role.ToLowerInvariant()}", role);
        var catalog = await SeedCatalogAsync(stock: 10m);
        var response = await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, 10m)]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Cierre_RBAC_exige_JWT_y_restringe_creacion_y_consulta()
    {
        await qa.ResetAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/cierres-diarios")).StatusCode);
        await LoginAsync("qa.close.forbidden", "SOLICITANTE");
        var catalog = await SeedCatalogAsync(stock: 10m);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/cierres-diarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, 10m)])).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
        await LoginAsync("qa.close.auditor", "AUDITOR");
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("api/cierres-diarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, 10m)])).StatusCode);
    }

    [Fact]
    public async Task Cierre_valida_estacion_fecha_y_mediciones_fisicas()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.close.invalid", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 10m);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await PostCloseAsync(Client, 999999, date, [(catalog.TankId, 10m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date.AddDays(1), [(catalog.TankId, 10m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date, [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, await PostCloseStatusAsync(Client, catalog.StationId, date, new { tanqueId = catalog.TankId, inventarioFisicoGalones = (decimal?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date, [(catalog.TankId, -1m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date, [(catalog.TankId, 101m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date, [(catalog.TankId, 1.234m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCloseAsync(Client, catalog.StationId, date, [(catalog.TankId, 10m), (catalog.TankId, 10m)])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("api/cierres-diarios", new { estacionId = catalog.StationId, fecha = "no-es-fecha", inventariosFisicos = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Cierre_doble_solicitud_concurrente_persiste_un_solo_acta()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.close.race.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 25m);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var supervisor = await LoginAsync("qa.close.race.supervisor", "SUPERVISOR");
        using var client1 = AuthenticatedClient(admin.Token);
        using var client2 = AuthenticatedClient(supervisor.Token);
        var responses = await Task.WhenAll(
            PostCloseAsync(client1, catalog.StationId, date, [(catalog.TankId, 25m)]),
            PostCloseAsync(client2, catalog.StationId, date, [(catalog.TankId, 25m)]));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>().CierresDiarios.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Movimiento_y_cierre_concurrentes_dejan_un_snapshot_conciliado(bool movimientoEntraPrimero)
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.close.movement.race.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var issued = await IssueTicketAsync(catalog, 5m);
        var dispatcher = await LoginAsync("qa.close.movement.race.dispatcher", "DESPACHADOR");
        Assert.True((await ValidateQrAsync(issued.Token)).GetProperty("valido").GetBoolean());
        var day = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var blocker = new NpgsqlConnection(Environment.GetEnvironmentVariable("QA_TEST_CONNECTION"));
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await using (var lockStation = blocker.CreateCommand())
        {
            lockStation.Transaction = blockerTransaction;
            lockStation.CommandText = "SELECT id_estacion FROM estaciones WHERE id_estacion = @station FOR UPDATE";
            lockStation.Parameters.AddWithValue("station", catalog.StationId);
            Assert.Equal(catalog.StationId, Convert.ToInt64(await lockStation.ExecuteScalarAsync()));
        }

        using var dispatchClient = AuthenticatedClient(dispatcher.Token);
        using var closeClient = AuthenticatedClient(admin.Token);
        var dispatchPayload = new { ticketId = issued.Id.ToString(), galonesServidos = 5m, tanqueId = catalog.TankId, estacionId = catalog.StationId, identidadConfirmada = true };
        Task<HttpResponseMessage> dispatchTask;
        Task<HttpResponseMessage> closeTask;
        if (movimientoEntraPrimero)
        {
            dispatchTask = dispatchClient.PostAsJsonAsync("api/despachos", dispatchPayload);
            await WaitForDatabaseLockWaitAsync(1);
            closeTask = PostCloseAsync(closeClient, catalog.StationId, day, [(catalog.TankId, 15m)]);
            await WaitForDatabaseLockWaitAsync(2);
        }
        else
        {
            closeTask = PostCloseAsync(closeClient, catalog.StationId, day, [(catalog.TankId, 20m)]);
            await WaitForDatabaseLockWaitAsync(1);
            dispatchTask = dispatchClient.PostAsJsonAsync("api/despachos", dispatchPayload);
            await WaitForDatabaseLockWaitAsync(2);
        }
        await blockerTransaction.CommitAsync();
        var dispatchResponse = await dispatchTask;
        var closeResponse = await closeTask;
        Assert.Equal(HttpStatusCode.Created, closeResponse.StatusCode);
        Assert.Contains(dispatchResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        await using var responseScope = qa.Factory.Services.CreateAsyncScope();
        var responseDb = responseScope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var saved = await responseDb.CierresDiarios.SingleAsync();
        if (dispatchResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(5m, saved.VolumenDespachadoGalones);
            Assert.Equal(1, saved.CantidadDespachos);
            Assert.Equal(15m, saved.InventarioFinalGalones);
            Assert.Equal(1, await responseDb.Despachos.CountAsync());
        }
        else
        {
            Assert.Equal(0m, saved.VolumenDespachadoGalones);
            Assert.Equal(0, saved.CantidadDespachos);
            Assert.Equal(20m, saved.InventarioFinalGalones);
            Assert.Equal(0, await responseDb.Despachos.CountAsync());
        }
        Assert.Equal(20m - saved.VolumenDespachadoGalones, saved.InventarioFinalGalones);
    }

    [Fact]
    public async Task Fallo_de_auditoria_revierte_el_cierre_y_su_inventario()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.close.rollback", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 15m);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION qa_rechazar_auditoria_cierre() RETURNS TRIGGER AS $$
                BEGIN IF NEW.accion = 'CIERRE_DIARIO_CREADO' THEN RAISE EXCEPTION 'QA cierre audit failure'; END IF; RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER qa_rechazar_auditoria_cierre BEFORE INSERT ON auditoria FOR EACH ROW EXECUTE FUNCTION qa_rechazar_auditoria_cierre();
                """);
        }
        try
        {
            var response = await PostCloseAsync(Client, catalog.StationId, DateOnly.FromDateTime(DateTime.UtcNow), [(catalog.TankId, 15m)]);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await using var checkScope = qa.Factory.Services.CreateAsyncScope();
            var db = checkScope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(0, await db.CierresDiarios.CountAsync());
            Assert.Equal(0, await db.Auditoria.CountAsync(x => x.Accion == "CIERRE_DIARIO_CREADO"));
            Assert.Equal(15m, await db.Tanques.Select(x => x.ExistenciaActualGalones).SingleAsync());
        }
        finally
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS qa_rechazar_auditoria_cierre ON auditoria; DROP FUNCTION IF EXISTS qa_rechazar_auditoria_cierre();");
        }
    }

    [Fact]
    public async Task Cierre_respeta_limites_utc_235959_y_medianoche_del_dia_siguiente()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.close.midnight", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 50m);
        foreach (var gallons in new[] { 2m, 3m })
        {
            var adjustment = await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = gallons, motivo = "Boundary fixture", usuarioId = 1 });
            Assert.Equal(HttpStatusCode.Created, adjustment.StatusCode);
        }
        long[] movementIds;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            movementIds = await db.MovimientosInventario.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
            var beforeMidnight = day.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
            var atMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            await db.Database.ExecuteSqlRawAsync($"UPDATE movimientos_inventario SET fecha_hora = TIMESTAMP '{beforeMidnight:yyyy-MM-dd HH:mm:ss}' WHERE id_movimiento = {movementIds[0]}");
            await db.Database.ExecuteSqlRawAsync($"UPDATE movimientos_inventario SET fecha_hora = TIMESTAMP '{atMidnight:yyyy-MM-dd HH:mm:ss}' WHERE id_movimiento = {movementIds[1]}");
        }
        var dayOne = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var firstDay = await Client.GetFromJsonAsync<JsonElement>($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={dayOne:yyyy-MM-dd}");
        var secondDay = await Client.GetFromJsonAsync<JsonElement>($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={dayOne.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(2m, firstDay.GetProperty("resumen").GetProperty("ajustesGalones").GetDecimal());
        Assert.Equal(52m, firstDay.GetProperty("resumen").GetProperty("inventarioTeoricoFinalGalones").GetDecimal());
        Assert.Equal(3m, secondDay.GetProperty("resumen").GetProperty("ajustesGalones").GetDecimal());
        Assert.Equal(55m, secondDay.GetProperty("resumen").GetProperty("inventarioTeoricoFinalGalones").GetDecimal());
    }

    [Fact]
    public async Task Cierre_por_movimientos_multiples_despachos_no_duplica_consumo()
    {
        await qa.ResetAsync();
        await LoginAsync("qa.close.dispatch.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var ticket1 = await IssueTicketAsync(catalog, 5m);
        var ticket2 = await IssueTicketAsync(catalog, 5m);
        await LoginAsync("qa.close.dispatcher", "DESPACHADOR");
        foreach (var ticket in new[] { ticket1, ticket2 })
        {
            Assert.True((await ValidateQrAsync(ticket.Token)).GetProperty("valido").GetBoolean());
            var response = await Client.PostAsJsonAsync("api/despachos", new { ticketId = ticket.Id.ToString(), galonesServidos = 5m, tanqueId = catalog.TankId, estacionId = catalog.StationId, identidadConfirmada = true });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = await Client.GetFromJsonAsync<JsonElement>($"api/cierres-diarios/resumen?estacionId={catalog.StationId}&fecha={date:yyyy-MM-dd}");
        Assert.Equal(10m, summary.GetProperty("resumen").GetProperty("volumenDespachadoGalones").GetDecimal());
        Assert.Equal(2, summary.GetProperty("resumen").GetProperty("cantidadDespachos").GetInt32());
        var created = await PostCloseAsync(Client, catalog.StationId, date, [(catalog.TankId, 10m)]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10m, saved.GetProperty("inventarioFinalGalones").GetDecimal());
    }

    [Fact]
    public async Task Cierres_admite_filtros_y_rechaza_rango_invertido()
    {
        await qa.ResetAsync();
        var actor = await LoginAsync("qa.close.filters", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 12m);
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created, (await PostCloseAsync(Client, catalog.StationId, day, [(catalog.TankId, 12m)])).StatusCode);
        var filter = await Client.GetAsync($"api/cierres-diarios?desde={day:yyyy-MM-dd}&hasta={day:yyyy-MM-dd}&estacionId={catalog.StationId}&usuarioId={actor.Id}");
        Assert.Equal(HttpStatusCode.OK, filter.StatusCode);
        var filteredRows = await filter.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(filteredRows);
        Assert.Single(filteredRows);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync($"api/cierres-diarios?desde={day.AddDays(1):yyyy-MM-dd}&hasta={day:yyyy-MM-dd}")).StatusCode);
    }

    private static Task<HttpResponseMessage> PostCloseAsync(HttpClient client, long stationId, DateOnly date, (long TankId, decimal? Gallons)[] physical) =>
        client.PostAsJsonAsync("api/cierres-diarios", new
        {
            estacionId = stationId,
            fecha = date.ToString("yyyy-MM-dd"),
            inventariosFisicos = physical.Select(x => new { tanqueId = x.TankId, inventarioFisicoGalones = x.Gallons }).ToArray(),
            observaciones = "Cierre de prueba QA"
        });

    private static async Task<HttpStatusCode> PostCloseStatusAsync(HttpClient client, long stationId, DateOnly date, object physical) =>
        (await client.PostAsJsonAsync("api/cierres-diarios", new { estacionId = stationId, fecha = date.ToString("yyyy-MM-dd"), inventariosFisicos = new[] { physical } })).StatusCode;

    private static async Task WaitForDatabaseLockWaitAsync(int minimum)
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("QA_TEST_CONNECTION"));
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 300; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= minimum) return;
            await Task.Delay(20);
        }
        Assert.Fail($"No aparecieron {minimum} esperas de bloqueo PostgreSQL dentro del tiempo límite.");
    }

    private static object Usuario(string username, string email, string name, string password, long roleId) => new { nombreUsuario = username, correo = email, nombreCompleto = name, password, telefono = "8095550100", rolId = roleId };
    private static object Empleado(string code, string name, string document, long departmentId) => new { codigoEmpleado = code, nombreCompleto = name, cedula = document, departamentoId = departmentId, correo = "qa.employee@example.test", cargo = "QA", telefonoMovil = "8095550100", activo = true };

    [Fact]
    public async Task Notificaciones_persisten_son_idempotentes_aisladas_y_leibles()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.notifications.admin", "ADMINISTRADOR");
        var supervisor = await LoginAsync("qa.notifications.supervisor", "SUPERVISOR");
        var key = $"test:event:{Guid.NewGuid():N}";
        async Task CreateAsync()
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<NotificacionService>();
            await service.CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR"], "AJUSTE_INVENTARIO", "Ajuste QA", "Aviso QA", "AVISO", "MOVIMIENTO_INVENTARIO", "44", key, new { movementId = 44 });
        }
        await Task.WhenAll(CreateAsync(), CreateAsync());
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(2, await db.Notificaciones.CountAsync(x => x.ClaveDeduplicacion == key));
        }

        Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/notificaciones")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/notificaciones")).StatusCode);
        var consulta = await LoginAsync("qa.notifications.consulta", "CONSULTA");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("api/notificaciones")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var listed = await Client.GetFromJsonAsync<JsonElement>("api/notificaciones?tipo=AJUSTE_INVENTARIO&leida=false");
        Assert.Equal(1, listed.GetProperty("total").GetInt32());
        var noticeId = listed.GetProperty("items")[0].GetProperty("id").GetInt64();
        Assert.Equal(1, (await Client.GetFromJsonAsync<JsonElement>("api/notificaciones/no-leidas")).GetProperty("cantidad").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"api/notificaciones/{noticeId}/leer", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"api/notificaciones/{noticeId}/leer", null)).StatusCode);
        Assert.Equal(0, (await Client.GetFromJsonAsync<JsonElement>("api/notificaciones/no-leidas")).GetProperty("cantidad").GetInt32());

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisor.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync($"api/notificaciones/{noticeId}/leer", null)).StatusCode);
        Assert.Equal(1, (await Client.GetFromJsonAsync<JsonElement>("api/notificaciones/no-leidas")).GetProperty("cantidad").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("api/notificaciones/leer-todas", null)).StatusCode);
        Assert.Equal(0, (await Client.GetFromJsonAsync<JsonElement>("api/notificaciones/no-leidas")).GetProperty("cantidad").GetInt32());
        Client.DefaultRequestHeaders.Authorization = null;
        _ = consulta;
    }

    [Fact]
    public async Task Worker_de_tickets_reutiliza_umbral_omite_terminales_y_deduplica_en_concurrencia()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.notifications.ticket.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 20m);
        var near = await IssueTicketAsync(catalog, 4m);
        var expired = await IssueTicketAsync(catalog, 3m);
        var cancelled = await IssueTicketAsync(catalog, 2m);
        var consumed = await IssueTicketAsync(catalog, 1m);
        _ = await LoginAsync("qa.notifications.ticket.supervisor", "SUPERVISOR");
        _ = await LoginAsync("qa.notifications.ticket.dispatcher", "DESPACHADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var qrValidation = await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = consumed.Token });
        Assert.Equal(HttpStatusCode.OK, qrValidation.StatusCode);
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = consumed.Id.ToString(), galonesServidos = 1m, tanqueId = catalog.TankId, identidadConfirmada = true });
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        var now = DateTime.UtcNow.AddMinutes(-1);
        var nearExpiry = DateTime.SpecifyKind(now.Add(TicketLifecycleService.ProximoAVencerThreshold), DateTimeKind.Unspecified);
        var expiredAt = DateTime.SpecifyKind(now.Add(TicketLifecycleService.ProximoAVencerThreshold).AddMinutes(10), DateTimeKind.Unspecified);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            foreach (var (id, expiry) in new[] { (near.Id, nearExpiry), (expired.Id, expiredAt), (cancelled.Id, expiredAt), (consumed.Id, expiredAt) })
                await db.Tickets.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.FechaVencimiento, expiry));
        }
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync($"api/tickets/{cancelled.Id}/anular", new { motivo = "No requerido" })).StatusCode);

        async Task<int> ProcessAsync()
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TicketNotificationProcessor>().ProcessDueAsync();
        }
        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(now.AddSeconds(-1), TimeSpan.Zero));
        await ProcessAsync();
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>().Notificaciones.CountAsync());

        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(now, TimeSpan.Zero));
        await Task.WhenAll(ProcessAsync(), ProcessAsync());
        qa.Factory.Clock.SetUtcNow(new DateTimeOffset(expiredAt, TimeSpan.Zero));
        await Task.WhenAll(ProcessAsync(), ProcessAsync());
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(3, await db.Notificaciones.CountAsync(x => x.Tipo == "TICKET_PROXIMO_A_VENCER"));
            Assert.Equal(6, await db.Notificaciones.CountAsync(x => x.Tipo == "TICKET_VENCIDO"));
            Assert.DoesNotContain(await db.Notificaciones.Select(x => x.ReferenciaId).ToListAsync(), id => id == cancelled.Id.ToString("D"));
            Assert.DoesNotContain(await db.Notificaciones.Select(x => x.ReferenciaId).ToListAsync(), id => id == consumed.Id.ToString("D"));
        }
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Inventario_bajo_crea_un_aviso_por_episodio_y_los_ajustes_se_enlazan_al_movimiento()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.notifications.stock.admin", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 10m);
        async Task<HttpResponseMessage> Adjust(string type, decimal gallons) => await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = type, cantidadGalones = gallons, motivo = "QA notificaciones", usuarioId = admin.Id });
        Assert.Equal(HttpStatusCode.Created, (await Adjust("AJUSTE_NEGATIVO", 6m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Adjust("MERMA", 0.5m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Adjust("AJUSTE_POSITIVO", 2m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Adjust("AJUSTE_NEGATIVO", 2m)).StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var notices = await db.Notificaciones.Where(x => x.UsuarioId == admin.Id).ToListAsync();
        Assert.Equal(2, notices.Count(x => x.Tipo == "INVENTARIO_BAJO"));
        var adjustments = notices.Where(x => x.Tipo == "AJUSTE_INVENTARIO").ToList();
        Assert.Equal(4, adjustments.Count);
        Assert.All(adjustments, row => Assert.StartsWith("movimiento:", row.ClaveDeduplicacion));
        Assert.Equal(4, await db.MovimientosInventario.CountAsync(x => x.ReferenciaTipo == "AJUSTE_MANUAL"));
        var supervisor = await LoginAsync("qa.notifications.stock.supervisor", "SUPERVISOR");
        using var adminClient = AuthenticatedClient(admin.Token);
        using var supervisorClient = AuthenticatedClient(supervisor.Token);
        Assert.Equal(HttpStatusCode.Created, (await adminClient.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 10m, motivo = "Recuperación QA", usuarioId = admin.Id })).StatusCode);
        var first = adminClient.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_NEGATIVO", cantidadGalones = 5m, motivo = "Carrera QA", usuarioId = admin.Id });
        var second = supervisorClient.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_NEGATIVO", cantidadGalones = 5m, motivo = "Carrera QA", usuarioId = supervisor.Id });
        var outcomes = await Task.WhenAll(first, second);
        Assert.All(outcomes, result => Assert.Equal(HttpStatusCode.Created, result.StatusCode));
        Assert.Equal(3, await db.Notificaciones.CountAsync(x => x.UsuarioId == admin.Id && x.Tipo == "INVENTARIO_BAJO"));
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Fallo_de_auditoria_revierte_ajuste_y_notificaciones_asociadas()
    {
        await qa.ResetAsync();
        var admin = await LoginAsync("qa.notifications.rollback", "ADMINISTRADOR");
        var catalog = await SeedCatalogAsync(stock: 10m);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION qa_rechazar_auditoria_ajuste() RETURNS TRIGGER AS $$
                BEGIN IF NEW.accion = 'INVENTORY_ADJUSTED' THEN RAISE EXCEPTION 'QA adjustment audit failure'; END IF; RETURN NEW; END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER qa_rechazar_auditoria_ajuste BEFORE INSERT ON auditoria FOR EACH ROW EXECUTE FUNCTION qa_rechazar_auditoria_ajuste();
                """);
        }
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = catalog.TankId, tipo = "AJUSTE_NEGATIVO", cantidadGalones = 6m, motivo = "Rollback QA", usuarioId = admin.Id }));
            await using var verify = qa.Factory.Services.CreateAsyncScope();
            var db = verify.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            Assert.Equal(10m, await db.Tanques.Where(x => x.Id == catalog.TankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
            Assert.Equal(0, await db.MovimientosInventario.CountAsync());
            Assert.Equal(0, await db.Notificaciones.CountAsync());
        }
        finally
        {
            await using var scope = qa.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS qa_rechazar_auditoria_ajuste ON auditoria; DROP FUNCTION IF EXISTS qa_rechazar_auditoria_ajuste();");
        }
    }

    private async Task<(long Id, string Token)> LoginAsync(string username, string role)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(ApiTestFactory.TestPassword, salt, 100_000, HashAlgorithmName.SHA256, 32);
        long userId;
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var roleRow = await db.Roles.SingleAsync(x => x.Nombre == role);
            var user = new Usuario { NombreUsuario = username, Correo = $"{username}@example.test", NombreCompleto = "QA Coverage User", Activo = true, PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}" };
            db.Usuarios.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
            db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = user.Id, RolId = roleRow.Id });
            await db.SaveChangesAsync();
        }
        var response = await Client.PostAsJsonAsync("api/login", new { usuario = username, contrasena = ApiTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("token").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (userId, token);
    }

    private async Task<Dictionary<string, long>> GetRolesAsync()
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        return await db.Roles.ToDictionaryAsync(x => x.Nombre, x => x.Id);
    }

    private async Task<CatalogSeed> SeedCatalogAsync(decimal stock = 0m, bool includeAlternates = false)
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var fuelIds = await db.TiposCombustible.Where(x => x.Activo).OrderBy(x => x.Id).Select(x => x.Id).Take(2).ToArrayAsync();
        var department = new Departamento { Codigo = "QA-COV-D1", Nombre = "QA Coverage Department", Activo = true };
        db.Departamentos.Add(department);
        await db.SaveChangesAsync();
        var employee = new Empleado { CodigoEmpleado = "QA-COV-E1", NombreCompleto = "QA Employee One", Cedula = "123-0000001-1", DepartamentoId = department.Id, Activo = true };
        var vehicle = new Vehiculo { Placa = "QA-COV-1", Ficha = "QA-COV-F1", Marca = "QA", Modelo = "Test", Anio = 2024, DepartamentoId = department.Id, CapacidadTanqueGalones = 100, OdometroKm = 0, Activo = true };
        db.Empleados.Add(employee);
        db.Vehiculos.Add(vehicle);
        await db.SaveChangesAsync();
        long department2 = 0, employee2 = 0, vehicle2 = 0;
        if (includeAlternates)
        {
            var alternateDepartment = new Departamento { Codigo = "QA-COV-D2", Nombre = "QA Coverage Department Two", Activo = true };
            db.Departamentos.Add(alternateDepartment);
            await db.SaveChangesAsync();
            var alternateEmployee = new Empleado { CodigoEmpleado = "QA-COV-E2", NombreCompleto = "QA Employee Two", Cedula = "123-0000002-2", DepartamentoId = alternateDepartment.Id, Activo = true };
            var alternateVehicle = new Vehiculo { Placa = "QA-COV-2", Ficha = "QA-COV-F2", Marca = "QA", Modelo = "Test Two", Anio = 2023, DepartamentoId = alternateDepartment.Id, CapacidadTanqueGalones = 80, OdometroKm = 10, Activo = true };
            db.Empleados.Add(alternateEmployee);
            db.Vehiculos.Add(alternateVehicle);
            await db.SaveChangesAsync();
            department2 = alternateDepartment.Id;
            employee2 = alternateEmployee.Id;
            vehicle2 = alternateVehicle.Id;
        }
        var station = new Estacion { Nombre = "QA Coverage Station", Ubicacion = "Local", Activo = true };
        db.Estaciones.Add(station);
        await db.SaveChangesAsync();
        var tank = new Tanque { Codigo = "QA-COV-T1", Nombre = "QA Tank", EstacionId = station.Id, TipoCombustibleId = fuelIds[0], CapacidadGalones = 100, ExistenciaActualGalones = stock, NivelCriticoGalones = 5, Activo = true };
        db.Tanques.Add(tank);
        await db.SaveChangesAsync();
        return new CatalogSeed(department.Id, employee.Id, vehicle.Id, fuelIds[0], station.Id, tank.Id, department2, employee2, vehicle2, includeAlternates ? fuelIds[1] : fuelIds[0]);
    }

    private async Task SetEmployeeContactAsync(long employeeId, string email, string phone)
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var employee = await db.Empleados.SingleAsync(x => x.Id == employeeId);
        employee.Correo = email;
        employee.TelefonoMovil = phone;
        await db.SaveChangesAsync();
    }

    private async Task<long> CreateApprovedRequestAsync(CatalogSeed catalog, decimal gallons, DateTime? expires = null)
    {
        var end = expires ?? DateTime.UtcNow.AddDays(3);
        var requestResponse = await Client.PostAsJsonAsync("api/solicitudes", new { empleadoId = catalog.EmployeeId, vehiculoId = catalog.VehicleId, departamentoId = catalog.DepartmentId, tipoCombustibleId = catalog.FuelId, cantidadSolicitadaGalones = gallons, fechaVencimiento = end, tipoSolicitud = "MANUAL", motivo = "QA coverage" });
        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        var id = (await requestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var approve = await Client.PutAsJsonAsync($"api/solicitudes/{id}/aprobar", new { cantidadAutorizadaGalones = gallons, fechaVencimiento = end, usuarioAprobadorId = 1 });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        return id;
    }

    private async Task<IssuedTicket> IssueTicketAsync(CatalogSeed catalog, decimal gallons)
    {
        var requestId = await CreateApprovedRequestAsync(catalog, gallons);
        var response = await Client.PostAsJsonAsync("api/tickets", new { solicitudId = requestId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new IssuedTicket(json.GetProperty("id").GetGuid(), json.GetProperty("qrToken").GetString()!, requestId);
    }

    private async Task<bool> IsQrValidAsync(string token) => (await ValidateQrAsync(token)).GetProperty("valido").GetBoolean();

    private async Task<JsonElement> ValidateQrAsync(string token)
    {
        var response = await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private HttpClient AuthenticatedClient(string token)
    {
        var client = qa.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static DateTime TruncateToMicrosecond(DateTime date) => DateTime.SpecifyKind(new DateTime(date.Ticks - date.Ticks % 10), DateTimeKind.Unspecified);

    private sealed record CatalogSeed(long DepartmentId, long EmployeeId, long VehicleId, long FuelId, long StationId, long TankId, long Department2Id, long Employee2Id, long Vehicle2Id, long Fuel2Id);
    private sealed record IssuedTicket(Guid Id, string Token, long RequestId);
}
