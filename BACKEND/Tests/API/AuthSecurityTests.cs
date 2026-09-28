using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using Xunit;

namespace TicketsCombustible.Api.Tests;

[Collection("QA database")]
public sealed class AuthSecurityTests(QaFixture qa)
{
    private HttpClient Client => qa.Client;

    [Fact]
    public async Task Refresh_rota_token_hash_y_reutilizacion_revoca_toda_la_familia()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.rotation");
        Assert.Equal("no-store, no-cache, max-age=0", login.Headers.CacheControl!.ToString());
        var original = await login.Content.ReadFromJsonAsync<JsonElement>();
        var firstRefresh = original.GetProperty("refreshToken").GetString()!;
        Assert.Equal(43, firstRefresh.Length);
        var access = original.GetProperty("token").GetString()!;
        Assert.NotEqual(firstRefresh, access);
        using (var scope = qa.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            var saved = await db.SesionesUsuario.SingleAsync();
            Assert.NotEqual(firstRefresh, saved.HashRefreshToken);
            Assert.Equal(64, saved.HashRefreshToken.Length);
        }

        var rotate = await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.Equal("no-store, no-cache, max-age=0", rotate.Headers.CacheControl!.ToString());
        var rotated = await rotate.Content.ReadFromJsonAsync<JsonElement>();
        var secondRefresh = rotated.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(firstRefresh, secondRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = firstRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = secondRefresh })).StatusCode);
        using var finalScope = qa.Factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(2, await finalDb.SesionesUsuario.CountAsync());
        Assert.All(await finalDb.SesionesUsuario.ToListAsync(), row => Assert.NotNull(row.RevocadoEn));
        Assert.Contains(await finalDb.Auditoria.Select(x => x.Accion).ToListAsync(), action => action == "REFRESH_REUSE_DETECTED");
    }

    [Fact]
    public async Task Dos_refresh_concurrentes_del_mismo_token_solo_rotan_una_vez_y_no_generan_500()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.concurrent");
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
        var responses = await Task.WhenAll(
            Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = token }),
            Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = token }));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);
        using var scope = qa.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(2, await db.SesionesUsuario.CountAsync());
        Assert.All(await db.SesionesUsuario.ToListAsync(), row => Assert.NotNull(row.RevocadoEn));
    }

    [Fact]
    public async Task Refresh_expirado_se_rechaza_con_respuesta_generica()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.expired");
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
        qa.Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow.AddDays(31));
        var response = await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("La sesión no es válida. Inicia sesión nuevamente.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Bootstrap_concurrente_crea_un_solo_administrador_y_no_responde_500()
    {
        await qa.ResetAsync();
        const string secret = "qa-bootstrap-secret-that-is-not-used-000000";
        var first = Client.PostAsJsonAsync("api/login/inicializar-admin", new
        {
            secreto = secret, usuario = "qa.bootstrap.concurrent.a", correo = "qa.bootstrap.concurrent.a@example.test",
            nombreCompleto = "QA Bootstrap A", contrasena = ApiTestFactory.TestPassword
        });
        var second = Client.PostAsJsonAsync("api/login/inicializar-admin", new
        {
            secreto = secret, usuario = "qa.bootstrap.concurrent.b", correo = "qa.bootstrap.concurrent.b@example.test",
            nombreCompleto = "QA Bootstrap B", contrasena = ApiTestFactory.TestPassword
        });
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);
        using var scope = qa.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(1, await (from userRole in db.UsuarioRoles join role in db.Roles on userRole.RolId equals role.Id where role.Nombre == "ADMINISTRADOR" select userRole.UsuarioId).Distinct().CountAsync());
        Assert.Equal(1, await db.Usuarios.CountAsync());
    }

    [Fact]
    public async Task Logout_es_idempotente_y_rechaza_renovacion_posterior()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.logout");
        var refresh = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync("api/login/logout", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync("api/login/logout", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task Logout_all_revoca_refresh_tokens_de_todas_las_sesiones_del_usuario()
    {
        await qa.ResetAsync();
        var first = await LoginAsync("qa.auth.logoutall");
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var second = await Client.PostAsJsonAsync("api/login", new { usuario = "qa.auth.logoutall", contrasena = ApiTestFactory.TestPassword });
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstBody.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync("api/login/logout-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = firstBody.GetProperty("refreshToken").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = secondBody.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [Fact]
    public async Task Logout_all_de_usuario_A_no_revoca_sesion_de_usuario_B()
    {
        await qa.ResetAsync();
        var userAResponse = await LoginAsync("qa.auth.isolation.a");
        var userA = await userAResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userBResponse = await LoginAsync("qa.auth.isolation.b");
        var userB = await userBResponse.Content.ReadFromJsonAsync<JsonElement>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync("api/login/logout-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = userA.GetProperty("refreshToken").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = userB.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [Fact]
    public async Task Logout_y_refresh_concurrentes_no_dejan_refresh_activo()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.logout-race");
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var refresh = body.GetProperty("refreshToken").GetString()!;
        using var firstClient = qa.Factory.CreateClient();
        using var secondClient = qa.Factory.CreateClient();
        var rotateTask = firstClient.PostAsJsonAsync("api/login/refresh", new { refreshToken = refresh });
        var logoutTask = secondClient.PostAsJsonAsync("api/login/logout", new { refreshToken = refresh });
        var outcomes = await Task.WhenAll(rotateTask, logoutTask);
        Assert.Contains(outcomes[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
        Assert.Equal(HttpStatusCode.NoContent, outcomes[1].StatusCode);
        Assert.DoesNotContain(outcomes, response => (int)response.StatusCode >= 500);
        var possibleReplacement = outcomes[0].StatusCode == HttpStatusCode.OK
            ? (await outcomes[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()
            : null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = possibleReplacement ?? refresh })).StatusCode);
    }

    [Fact]
    public async Task Reset_password_y_refresh_concurrentes_no_dejan_refresh_activo()
    {
        await qa.ResetAsync();
        var userLogin = await LoginAsync("qa.auth.reset-race");
        var user = await userLogin.Content.ReadFromJsonAsync<JsonElement>();
        var refresh = user.GetProperty("refreshToken").GetString()!;
        var adminToken = await CreateAdminAndLoginAsync("qa.auth.reset-admin");
        using var userClient = qa.Factory.CreateClient();
        using var adminClient = qa.Factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var rotateTask = userClient.PostAsJsonAsync("api/login/refresh", new { refreshToken = refresh });
        var resetTask = adminClient.PostAsJsonAsync($"api/gestion/usuarios/{user.GetProperty("id").GetInt64()}/restablecer-contrasena", new { contrasena = "Qa-reset-race-password-2026" });
        var outcomes = await Task.WhenAll(rotateTask, resetTask);
        Assert.Contains(outcomes[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
        Assert.Equal(HttpStatusCode.NoContent, outcomes[1].StatusCode);
        Assert.DoesNotContain(outcomes, response => (int)response.StatusCode >= 500);
        var possibleReplacement = outcomes[0].StatusCode == HttpStatusCode.OK
            ? (await outcomes[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()
            : null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = possibleReplacement ?? refresh })).StatusCode);
    }

    [Fact]
    public async Task Desactivar_usuario_y_refresh_concurrentes_no_dejan_refresh_activo()
    {
        await qa.ResetAsync();
        var userLogin = await LoginAsync("qa.auth.disable-race");
        var user = await userLogin.Content.ReadFromJsonAsync<JsonElement>();
        var refresh = user.GetProperty("refreshToken").GetString()!;
        var adminToken = await CreateAdminAndLoginAsync("qa.auth.disable-admin");
        using var userClient = qa.Factory.CreateClient();
        using var adminClient = qa.Factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var rotateTask = userClient.PostAsJsonAsync("api/login/refresh", new { refreshToken = refresh });
        var disableTask = adminClient.DeleteAsync($"api/gestion/usuarios/{user.GetProperty("id").GetInt64()}");
        var outcomes = await Task.WhenAll(rotateTask, disableTask);
        Assert.Contains(outcomes[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
        Assert.Equal(HttpStatusCode.NoContent, outcomes[1].StatusCode);
        Assert.DoesNotContain(outcomes, response => (int)response.StatusCode >= 500);
        var possibleReplacement = outcomes[0].StatusCode == HttpStatusCode.OK
            ? (await outcomes[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()
            : null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = possibleReplacement ?? refresh })).StatusCode);
    }

    [Fact]
    public async Task Cambio_y_reset_de_contrasena_revocan_refresh_activos()
    {
        await qa.ResetAsync();
        var login = await LoginAsync("qa.auth.password");
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var originalRefresh = body.GetProperty("refreshToken").GetString()!;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync("api/login/cambiar-contrasena", new { contrasenaActual = ApiTestFactory.TestPassword, nuevaContrasena = "Qa-new-password-2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = originalRefresh })).StatusCode);
        var reLogin = await LoginExistingAsync("qa.auth.password", "Qa-new-password-2026");
        var reLoginBody = await reLogin.Content.ReadFromJsonAsync<JsonElement>();
        var adminToken = await CreateAdminAndLoginAsync("qa.auth.admin");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync($"api/gestion/usuarios/{body.GetProperty("id").GetInt64()}/restablecer-contrasena", new { contrasena = "Qa-reset-password-2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = reLoginBody.GetProperty("refreshToken").GetString() })).StatusCode);
    }

    [Fact]
    public async Task Usuario_desactivado_no_puede_renovar_y_claim_de_rol_viejo_se_rechaza()
    {
        await qa.ResetAsync();
        var userResponse = await LoginAsync("qa.auth.disabled");
        var userBody = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = userBody.GetProperty("id").GetInt64();
        var adminToken = await CreateAdminAndLoginAsync("qa.auth.admin2");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"api/gestion/usuarios/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = userBody.GetProperty("refreshToken").GetString() })).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBody.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
    }

    [Fact]
    public async Task Cambio_de_rol_invalida_claims_y_refresh_antiguos_y_overposting_no_cambia_estado_interno()
    {
        await qa.ResetAsync();
        var targetLogin = await LoginAsync("qa.auth.rolechange");
        var target = await targetLogin.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetInt64();
        var oldAccess = target.GetProperty("token").GetString()!;
        var oldRefresh = target.GetProperty("refreshToken").GetString()!;
        var adminToken = await CreateAdminAndLoginAsync("qa.auth.roleadmin");
        using var scope = qa.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var consultaId = await db.Roles.Where(role => role.Nombre == "CONSULTA").Select(role => role.Id).SingleAsync();
        var beforeHash = await db.Usuarios.Where(user => user.Id == targetId).Select(user => user.PasswordHash).SingleAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var update = await Client.PutAsJsonAsync($"api/gestion/usuarios/{targetId}", new
        {
            correo = "qa.auth.rolechange@example.test", nombreCompleto = "QA Changed Role", rolId = consultaId,
            activo = false, passwordHash = "attacker", esAdmin = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = oldRefresh })).StatusCode);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldAccess);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("api/catalogos/roles")).StatusCode);
        var persisted = await db.Usuarios.SingleAsync(user => user.Id == targetId);
        Assert.True(persisted.Activo);
        Assert.Equal(beforeHash, persisted.PasswordHash);
        Assert.Equal("CONSULTA", await (from userRole in db.UsuarioRoles join role in db.Roles on userRole.RolId equals role.Id where userRole.UsuarioId == targetId select role.Nombre).SingleAsync());
    }

    [Fact]
    public async Task Login_no_filtra_hash_y_aplica_headers_de_seguridad_y_no_store()
    {
        await qa.ResetAsync();
        var response = await LoginAsync("qa.auth.headers");
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiTestFactory.JwtSecret, json, StringComparison.Ordinal);
        var accessToken = JsonDocument.Parse(json).RootElement.GetProperty("token").GetString()!;
        var parsedToken = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal("TicketsCombustible.Api", parsedToken.Issuer);
        Assert.Contains("LaVomVa.Client", parsedToken.Audiences);
        Assert.Contains(parsedToken.Claims, claim => claim.Type == JwtRegisteredClaimNames.Jti && !string.IsNullOrWhiteSpace(claim.Value));
        Assert.Contains(parsedToken.Claims, claim => claim.Type == JwtRegisteredClaimNames.Iat);
        Assert.InRange((parsedToken.ValidTo - DateTime.UtcNow).TotalMinutes, 14.8, 15.1);
        Assert.Equal("no-store, no-cache, max-age=0", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Entrada_auth_malformada_se_rechaza_sin_detalles_internos()
    {
        await qa.ResetAsync();
        using var malformed = new StringContent("{\"usuario\":", System.Text.Encoding.UTF8, "application/json");
        var badJson = await Client.PostAsync("api/login", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);
        var body = await badJson.Content.ReadAsStringAsync();
        Assert.DoesNotContain("at TicketsCombustible", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", body, StringComparison.OrdinalIgnoreCase);
        using var wrongType = new StringContent("usuario=qa", System.Text.Encoding.UTF8, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Client.PostAsync("api/login", wrongType)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync("api/login/refresh", new { refreshToken = new string('x', 200_000) })).StatusCode);
    }

    [Fact]
    public async Task CORS_rechaza_origin_arbitrario_y_ids_invalidos_no_producen_500()
    {
        await qa.ResetAsync();
        using var corsRequest = new HttpRequestMessage(HttpMethod.Options, "api/login");
        corsRequest.Headers.Add("Origin", "https://attacker.invalid");
        corsRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var cors = await Client.SendAsync(corsRequest);
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Credentials"));

        var adminToken = await CreateAdminAndLoginAsync("qa.auth.ids");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("api/tickets/not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync("api/gestion/usuarios/-1", new { correo = "invalid@example.test", nombreCompleto = "Invalid", rolId = 1 })).StatusCode);
        var traversal = await Client.GetAsync("api/reportes/exportar?tipo=consumo&formato=pdf&desde=..%2F..%2Fetc%2Fpasswd");
        Assert.DoesNotContain((int)traversal.StatusCode, new[] { 500, 502, 503, 504 });
    }

    [Fact]
    public async Task Entradas_SQL_y_HTML_se_guardan_como_datos_y_consultas_siguen_parametrizadas()
    {
        await qa.ResetAsync();
        var response = await Client.PostAsJsonAsync("api/login/registro", new
        {
            usuario = "qa'; OR 1=1 --", correo = "sql.qa@example.test", nombreCompleto = "<script>alert(1)</script>", contrasena = ApiTestFactory.TestPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = qa.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var saved = await db.Usuarios.SingleAsync(x => x.Correo == "sql.qa@example.test");
        Assert.Equal("<script>alert(1)</script>", saved.NombreCompleto);
        Assert.Equal("qa'; OR 1=1 --", saved.NombreUsuario);
        Assert.Equal(1, await db.Usuarios.CountAsync());
    }

    private async Task<HttpResponseMessage> LoginAsync(string username, string password = ApiTestFactory.TestPassword)
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var role = await db.Roles.SingleAsync(x => x.Nombre == "DESPACHADOR");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var user = new Usuario { NombreUsuario = username, Correo = $"{username}@example.test", NombreCompleto = "QA Security", PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}", Activo = true };
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = user.Id, RolId = role.Id });
        await db.SaveChangesAsync();
        return await Client.PostAsJsonAsync("api/login", new { usuario = username, contrasena = password });
    }

    private async Task<string> CreateAdminAndLoginAsync(string username)
    {
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var role = await db.Roles.SingleAsync(x => x.Nombre == "ADMINISTRADOR");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(ApiTestFactory.TestPassword, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var user = new Usuario { NombreUsuario = username, Correo = $"{username}@example.test", NombreCompleto = "QA Admin", PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}", Activo = true };
        db.Usuarios.Add(user); await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = user.Id, RolId = role.Id }); await db.SaveChangesAsync();
        var response = await Client.PostAsJsonAsync("api/login", new { usuario = username, contrasena = ApiTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private Task<HttpResponseMessage> LoginExistingAsync(string username, string password) => Client.PostAsJsonAsync("api/login", new { usuario = username, contrasena = password });
}
