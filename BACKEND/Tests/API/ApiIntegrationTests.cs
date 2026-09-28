using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using Xunit;

namespace TicketsCombustible.Api.Tests;

[CollectionDefinition("QA database", DisableParallelization = true)]
public sealed class QaDatabaseCollection : ICollectionFixture<QaFixture> { }

public sealed class QaFixture : IAsyncLifetime
{
    public ApiTestFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("QA_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Define QA_TEST_CONNECTION hacia una base temporal lavomva_test.");
        Factory = new ApiTestFactory(connection);
        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        Factory.ResetProviders();
        await Factory.ResetDatabaseAsync();
    }
}

[Collection("QA database")]
public sealed class ApiIntegrationTests(QaFixture qa)
{
    private HttpClient Client => qa.Client;

    [Fact]
    public async Task Politica_TLS_redirige_HTTP_y_emite_HSTS_en_transporte_HTTPS()
    {
        await qa.ResetAsync();
        await using var production = new ApiTestFactory(Environment.GetEnvironmentVariable("QA_TEST_CONNECTION")!, "Production");
        using var http = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });

        var redirect = await http.GetAsync("api/no-route");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirect.StatusCode);
        Assert.Equal("https", redirect.Headers.Location?.Scheme);
        Assert.Equal(5443, redirect.Headers.Location?.Port);

        var secure = await production.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/no-route";
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("lavomva.test");
        });
        Assert.Equal("max-age=2592000", secure.Response.Headers["Strict-Transport-Security"].ToString());
    }

    [Fact]
    public async Task Registro_persiste_usuario_inactivo_con_hash_y_login_rechaza_hasta_activacion()
    {
        await qa.ResetAsync();
        var response = await Client.PostAsJsonAsync("api/login/registro", new
        {
            usuario = "qa.solicitante", correo = "qa.solicitante@example.test", nombreCompleto = "QA Solicitante",
            contrasena = ApiTestFactory.TestPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var saved = await db.Usuarios.SingleAsync(x => x.NombreUsuario == "qa.solicitante");
        Assert.False(saved.Activo);
        Assert.StartsWith("PBKDF2$100000$", saved.PasswordHash);
        Assert.DoesNotContain(ApiTestFactory.TestPassword, saved.PasswordHash);
        Assert.Equal("CONSULTA", await (from ur in db.UsuarioRoles join role in db.Roles on ur.RolId equals role.Id where ur.UsuarioId == saved.Id select role.Nombre).SingleAsync());
        var login = await Client.PostAsJsonAsync("api/login", new { usuario = saved.NombreUsuario, contrasena = ApiTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        var duplicate = await Client.PostAsJsonAsync("api/login/registro", new
        {
            usuario = "qa.solicitante", correo = "qa.other@example.test", nombreCompleto = "Otro QA", contrasena = ApiTestFactory.TestPassword
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task RBAC_bloquea_sin_sesion_y_responde_con_roles_actuales_de_base_de_datos()
    {
        await qa.ResetAsync();
        var anonymous = await Client.GetAsync("api/catalogos/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var admin = await AddUserAndLoginAsync("qa.admin", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        var missingTicket = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"api/tickets/{missingTicket}/qr")).StatusCode);
        var alteredParts = admin.Token.Split('.');
        alteredParts[2] = (alteredParts[2][0] == 'A' ? "B" : "A") + alteredParts[2][1..];
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', alteredParts));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        var wrongRole = new JwtSecurityToken(issuer: "TicketsCombustible.Api", audience: "LaVomVa.Client", claims: new[] { new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Role, "ADMINISTRADOR"), new Claim(ClaimTypes.Role, "CONSULTA") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiTestFactory.JwtSecret)), SecurityAlgorithms.HmacSha256));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(wrongRole));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        var expired = new JwtSecurityToken(issuer: "TicketsCombustible.Api", audience: "LaVomVa.Client", claims: new[] { new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Role, "ADMINISTRADOR") },
            expires: DateTime.UtcNow.AddMinutes(-5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiTestFactory.JwtSecret)), SecurityAlgorithms.HmacSha256));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        var dispatcher = await AddUserAndLoginAsync("qa.dispatcher", "DESPACHADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dispatcher.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"api/tickets/{missingTicket}/qr")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var user = await db.Usuarios.SingleAsync(x => x.NombreUsuario == "qa.admin");
            user.Activo = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Solicitud_aprobacion_ticket_qr_y_persistencia_conservan_el_flujo()
    {
        await qa.ResetAsync();
        var admin = await AddUserAndLoginAsync("qa.admin", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var departmentResponse = await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA", nombre = "QA Operaciones", activo = true });
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode);
        var department = await departmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var departmentId = department.GetProperty("id").GetInt64();
        var employeeResponse = await Client.PostAsJsonAsync("api/gestion/empleados", new { codigoEmpleado = "QA-001", nombreCompleto = "Persona de Prueba", cedula = "000-0000000-0", departamentoId = departmentId, activo = true });
        Assert.Equal(HttpStatusCode.Created, employeeResponse.StatusCode);
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var vehicleResponse = await Client.PostAsJsonAsync("api/gestion/vehiculos", new { placa = "QA-0001", ficha = "QA-1", marca = "QA", modelo = "Test", anio = 2024, tipo = "SUV", departamentoId = departmentId, capacidadTanqueGalones = 20, odometroKm = 0, activo = true });
        Assert.Equal(HttpStatusCode.Created, vehicleResponse.StatusCode);
        var vehicleId = (await vehicleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var fuelType = await db.TiposCombustible.Where(x => x.Activo).Select(x => x.Id).FirstAsync();
        var expiry = DateTime.UtcNow.AddDays(2);
        var requestResponse = await Client.PostAsJsonAsync("api/solicitudes", new { empleadoId = employeeId, vehiculoId = vehicleId, departamentoId = departmentId, tipoCombustibleId = fuelType, cantidadSolicitadaGalones = 10m, fechaVencimiento = expiry, tipoSolicitud = "MANUAL", motivo = "Prueba QA" });
        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        var requestId = (await requestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 8m, fechaVencimiento = expiry, usuarioAprobadorId = admin.Id })).StatusCode);
        var ticketResponse = await Client.PostAsJsonAsync("api/tickets", new { solicitudId = requestId, usuarioEmisorId = admin.Id });
        Assert.Equal(HttpStatusCode.Created, ticketResponse.StatusCode);
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(ticket.TryGetProperty("qrToken", out _));
        Assert.False(ticket.TryGetProperty("qrHash", out _));
        var ticketId = ticket.GetProperty("id").GetGuid();
        Assert.Equal("COM-2026-000001", ticket.GetProperty("numeroSecuencial").GetString());
        Assert.Equal(8m, ticket.GetProperty("cantidadAutorizadaGalones").GetDecimal());
        Assert.Equal("CREADO", ticket.GetProperty("estado").GetString());
        var qr = await Client.GetAsync($"api/tickets/{ticketId}/qr");
        Assert.Equal("image/png", qr.Content.Headers.ContentType?.MediaType);
        Assert.True((await qr.Content.ReadAsByteArrayAsync()).Length > 100);
        var token = await db.Tickets.Where(x => x.Id == ticketId).Select(x => x.QrToken).SingleAsync();
        var validation = await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = token });
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.True((await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        var reapprove = await Client.PutAsJsonAsync($"api/solicitudes/{requestId}/aprobar", new { cantidadAutorizadaGalones = 8m, fechaVencimiento = expiry, usuarioAprobadorId = admin.Id });
        Assert.Equal(HttpStatusCode.Conflict, reapprove.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET cantidad_autorizada_galones = 7 WHERE id_ticket = {ticketId}");
        var altered = await (await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = token })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(altered.GetProperty("valido").GetBoolean());
        Assert.Equal(1, await db.Tickets.CountAsync());
        Assert.Equal(1, await db.Solicitudes.CountAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Despacho_y_recepcion_actualizan_existencia_y_movimientos_persistidos()
    {
        await qa.ResetAsync();
        var admin = await AddUserAndLoginAsync("qa.admin", "ADMINISTRADOR");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var department = await (await Client.PostAsJsonAsync("api/gestion/departamentos", new { codigo = "QA", nombre = "QA Operaciones", activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        var depId = department.GetProperty("id").GetInt64();
        var employee = await (await Client.PostAsJsonAsync("api/gestion/empleados", new { codigoEmpleado = "QA-002", nombreCompleto = "Persona QA", cedula = "111-1111111-1", departamentoId = depId, activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        var veh = await (await Client.PostAsJsonAsync("api/gestion/vehiculos", new { placa = "QA-0002", ficha = "QA-2", marca = "QA", modelo = "Test", departamentoId = depId, odometroKm = 0, activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        var station = await (await Client.PostAsJsonAsync("api/gestion/estaciones", new { nombre = "Estación QA", ubicacion = "Laboratorio", activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var fuelId = await db.TiposCombustible.Where(x => x.Activo).Select(x => x.Id).FirstAsync();
        var tank = await (await Client.PostAsJsonAsync("api/gestion/tanques", new { codigo = "QA-T1", nombre = "Tanque QA", estacionId = station.GetProperty("id").GetInt64(), tipoCombustibleId = fuelId, capacidadGalones = 100m, existenciaActualGalones = 0m, nivelCriticoGalones = 5m, activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        var tankId = tank.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("api/inventario/ajustes", new { tanqueId = tankId, tipo = "AJUSTE_POSITIVO", cantidadGalones = 30m, motivo = "Carga inicial QA", usuarioId = admin.Id })).StatusCode);
        var adjustmentMovement = await db.MovimientosInventario.SingleAsync(x => x.TanqueId == tankId);
        Assert.Equal("AJUSTE_POSITIVO", adjustmentMovement.TipoMovimiento);
        Assert.Equal("AJUSTE_MANUAL", adjustmentMovement.ReferenciaTipo);
        Assert.Equal(30m, adjustmentMovement.CantidadGalones);
        Assert.Equal(admin.Id, adjustmentMovement.UsuarioId);
        Assert.NotEqual(default, adjustmentMovement.FechaHora);
        var supplier = await (await Client.PostAsJsonAsync("api/recepciones/proveedores", new { nombre = "Proveedor QA", rnc = "000000000", activo = true })).Content.ReadFromJsonAsync<JsonElement>();
        var receive = await Client.PostAsJsonAsync("api/recepciones", new { proveedorId = supplier.GetProperty("id").GetInt64(), numeroFactura = "QA-FACT-001", fechaRecepcion = DateTime.UtcNow, observaciones = "recepción QA", detalles = new[] { new { tanqueId = tankId, volumenRecibidoGalones = 10m, costoUnitario = 1m } } });
        Assert.Equal(HttpStatusCode.Created, receive.StatusCode);
        Assert.Equal(40m, await db.Tanques.Where(x => x.Id == tankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Assert.Equal(2, await db.MovimientosInventario.CountAsync(x => x.TanqueId == tankId));
        var receptionMovement = await db.MovimientosInventario.SingleAsync(x => x.TanqueId == tankId && x.ReferenciaTipo == "RECEPCION");
        Assert.Equal("ENTRADA", receptionMovement.TipoMovimiento);
        Assert.Equal(10m, receptionMovement.CantidadGalones);
        Assert.False(string.IsNullOrWhiteSpace(receptionMovement.ReferenciaId));
        Assert.Equal(admin.Id, receptionMovement.UsuarioId);

        var expiry = DateTime.UtcNow.AddDays(2);
        var request = await (await Client.PostAsJsonAsync("api/solicitudes", new { empleadoId = employee.GetProperty("id").GetInt64(), vehiculoId = veh.GetProperty("id").GetInt64(), departamentoId = depId, tipoCombustibleId = fuelId, cantidadSolicitadaGalones = 10m, fechaVencimiento = expiry, tipoSolicitud = "MANUAL" })).Content.ReadFromJsonAsync<JsonElement>();
        var reqId = request.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/solicitudes/{reqId}/aprobar", new { cantidadAutorizadaGalones = 8m, fechaVencimiento = expiry, usuarioAprobadorId = admin.Id })).StatusCode);
        var ticket = await (await Client.PostAsJsonAsync("api/tickets", new { solicitudId = reqId })).Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = ticket.GetProperty("id").GetGuid();
        Assert.False(ticket.TryGetProperty("qrToken", out _));
        Assert.False(ticket.TryGetProperty("qrHash", out _));
        var token = await db.Tickets.Where(x => x.Id == ticketId).Select(x => x.QrToken).SingleAsync();
        Assert.True((await (await Client.PostAsJsonAsync("api/tickets/validar", new { qrData = token })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        var dispatch = await Client.PostAsJsonAsync("api/despachos", new { ticketId = ticketId.ToString(), galonesServidos = 6m, identidadConfirmada = true, tanqueId = tankId });
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        Assert.Equal(34m, await db.Tanques.Where(x => x.Id == tankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Assert.Equal(1, await db.Despachos.CountAsync(x => x.TicketId == ticketId));
        Assert.Equal(3, await db.MovimientosInventario.CountAsync(x => x.TanqueId == tankId));
        var dispatchMovement = await db.MovimientosInventario.SingleAsync(x => x.TanqueId == tankId && x.ReferenciaTipo == "DESPACHO");
        Assert.Equal("SALIDA", dispatchMovement.TipoMovimiento);
        Assert.Equal(6m, dispatchMovement.CantidadGalones);
        Assert.False(string.IsNullOrWhiteSpace(dispatchMovement.ReferenciaId));
        Assert.NotEqual(default, dispatchMovement.FechaHora);
        Assert.Equal("CONSUMIDO", (await db.Tickets.Where(x => x.Id == ticketId).Select(x => x.Estado).SingleAsync()).ToString());
        var replay = await Client.PostAsJsonAsync("api/despachos", new { ticketId = ticketId.ToString(), galonesServidos = 6m, identidadConfirmada = true, tanqueId = tankId });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        var secondRequest = await (await Client.PostAsJsonAsync("api/solicitudes", new { empleadoId = employee.GetProperty("id").GetInt64(), vehiculoId = veh.GetProperty("id").GetInt64(), departamentoId = depId, tipoCombustibleId = fuelId, cantidadSolicitadaGalones = 10m, fechaVencimiento = expiry, tipoSolicitud = "MANUAL" })).Content.ReadFromJsonAsync<JsonElement>();
        var secondRequestId = secondRequest.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"api/solicitudes/{secondRequestId}/aprobar", new { cantidadAutorizadaGalones = 8m, fechaVencimiento = expiry, usuarioAprobadorId = admin.Id })).StatusCode);
        var secondTicket = await (await Client.PostAsJsonAsync("api/tickets", new { solicitudId = secondRequestId })).Content.ReadFromJsonAsync<JsonElement>();
        var secondTicketId = secondTicket.GetProperty("id").GetGuid();
        Assert.False(secondTicket.TryGetProperty("qrToken", out _));
        Assert.False(secondTicket.TryGetProperty("qrHash", out _));
        var secondQr = await db.Tickets.Where(x => x.Id == secondTicketId).Select(x => x.QrToken).SingleAsync();
        using var clientA = qa.Factory.CreateClient();
        using var clientB = qa.Factory.CreateClient();
        var operatorA = await AddUserAndLoginAsync("qa.dispatcher.a", "DESPACHADOR");
        var operatorB = await AddUserAndLoginAsync("qa.dispatcher.b", "DESPACHADOR");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorA.Token);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorB.Token);
        Assert.True((await (await clientA.PostAsJsonAsync("api/tickets/validar", new { qrData = secondQr })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        Assert.True((await (await clientB.PostAsJsonAsync("api/tickets/validar", new { qrData = secondQr })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valido").GetBoolean());
        var firstAttempt = clientA.PostAsJsonAsync("api/despachos", new { ticketId = secondTicketId.ToString(), galonesServidos = 4m, identidadConfirmada = true, tanqueId = tankId });
        var secondAttempt = clientB.PostAsJsonAsync("api/despachos", new { ticketId = secondTicketId.ToString(), galonesServidos = 4m, identidadConfirmada = true, tanqueId = tankId });
        var concurrentResponses = await Task.WhenAll(firstAttempt, secondAttempt);
        Assert.Single(concurrentResponses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrentResponses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, await db.Despachos.CountAsync());
        Assert.Equal(4, await db.MovimientosInventario.CountAsync(x => x.TanqueId == tankId));
        Assert.Equal(30m, await db.Tanques.Where(x => x.Id == tankId).Select(x => x.ExistenciaActualGalones).SingleAsync());
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<(long Id, string Token)> AddUserAndLoginAsync(string username, string roleName)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(ApiTestFactory.TestPassword, salt, 100_000, HashAlgorithmName.SHA256, 32);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Nombre == roleName);
            var user = new Usuario { NombreUsuario = username, Correo = $"{username}@example.test", NombreCompleto = "QA Test User", Activo = true,
                PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}" };
            db.Usuarios.Add(user);
            await db.SaveChangesAsync();
            db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = user.Id, RolId = role.Id });
            await db.SaveChangesAsync();
        }
        var login = await Client.PostAsJsonAsync("api/login", new { usuario = username, contrasena = ApiTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetInt64(), body.GetProperty("token").GetString()!);
    }
}
