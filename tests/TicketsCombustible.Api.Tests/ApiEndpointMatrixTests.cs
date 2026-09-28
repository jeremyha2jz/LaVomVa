using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using Xunit;

namespace TicketsCombustible.Api.Tests;

[Collection("QA database")]
public sealed class ApiEndpointMatrixTests(QaFixture qa)
{
    private static readonly string[] Roles = ["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR", "SOLICITANTE", "AUDITOR", "CONSULTA"];
    private static readonly string[] AdminSupervisor = ["ADMINISTRADOR", "SUPERVISOR"];
    private static readonly string[] AdminSupervisorDispatcher = ["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR"];
    private static readonly string[] AdminAuditor = ["ADMINISTRADOR", "AUDITOR"];
    private static readonly string[] AdminSupervisorDispatcherAuditor = ["ADMINISTRADOR", "SUPERVISOR", "DESPACHADOR", "AUDITOR"];

    // Null means any authenticated role; an empty array means intentionally public.
    private static readonly IReadOnlyDictionary<string, string[]?> Matrix = BuildMatrix();

    [Fact]
    public async Task Bootstrap_es_publico_pero_solo_crea_un_administrador_con_secreto_valido()
    {
        await qa.ResetAsync();
        using var client = qa.Factory.CreateClient();
        var data = new { usuario = "qa.bootstrap.admin", correo = "qa.bootstrap@example.test", nombreCompleto = "QA Bootstrap", contrasena = ApiTestFactory.TestPassword };

        var wrong = await client.PostAsJsonAsync("api/login/inicializar-admin", new { secreto = "incorrecto", data.usuario, data.correo, data.nombreCompleto, data.contrasena });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var first = await client.PostAsJsonAsync("api/login/inicializar-admin", new { secreto = "qa-bootstrap-secret-that-is-not-used-000000", data.usuario, data.correo, data.nombreCompleto, data.contrasena });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var responseText = await first.Content.ReadAsStringAsync();
        Assert.DoesNotContain("qa-bootstrap-secret", responseText, StringComparison.OrdinalIgnoreCase);

        var second = await client.PostAsJsonAsync("api/login/inicializar-admin", new { secreto = "qa-bootstrap-secret-that-is-not-used-000000", usuario = "qa.bootstrap.second", correo = "qa.bootstrap.second@example.test", nombreCompleto = "QA Second", contrasena = ApiTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        Assert.Equal(1, await (from ur in db.UsuarioRoles join role in db.Roles on ur.RolId equals role.Id where role.Nombre == "ADMINISTRADOR" select ur.UsuarioId).Distinct().CountAsync());
        Assert.False(await db.Usuarios.AnyAsync(x => x.NombreUsuario == "qa.bootstrap.second"));
    }

    [Fact]
    public void OpenAPI_se_genera_y_publica_los_metodos_de_las_rutas_principales()
    {
        var document = qa.Factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Contains("/api/tickets", document.Paths.Keys);
        Assert.Contains("/api/cierres-diarios", document.Paths.Keys);
        Assert.Contains("/api/reportes/exportar", document.Paths.Keys);
        Assert.True(document.Paths["/api/tickets"].Operations.ContainsKey(Microsoft.OpenApi.Models.OperationType.Get));
        Assert.True(document.Paths["/api/tickets"].Operations.ContainsKey(Microsoft.OpenApi.Models.OperationType.Post));
        Assert.True(document.Paths["/api/cierres-diarios"].Operations.ContainsKey(Microsoft.OpenApi.Models.OperationType.Get));
        Assert.True(document.Paths["/api/cierres-diarios"].Operations.ContainsKey(Microsoft.OpenApi.Models.OperationType.Post));
    }

    [Fact]
    public async Task Catalogos_ignoran_ids_y_estado_interno_en_los_contratos_de_creacion()
    {
        await qa.ResetAsync();
        var identities = await CreateRoleUsersAsync();
        using var client = qa.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identities["ADMINISTRADOR"]);
        var departmentResponse = await client.PostAsJsonAsync("api/gestion/departamentos", new { id = 999999, codigo = "QA-MASS", nombre = "QA mass assignment", descripcion = "test", activo = true });
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode);
        var department = await departmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(999999L, department.GetProperty("id").GetInt64());

        var stationResponse = await client.PostAsJsonAsync("api/gestion/estaciones", new { id = 999998, nombre = "QA Station", ubicacion = "QA", activo = true });
        Assert.Equal(HttpStatusCode.Created, stationResponse.StatusCode);
        var station = await stationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var stationId = station.GetProperty("id").GetInt64();
        Assert.NotEqual(999998L, stationId);

        await using var scope = qa.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        var fuelId = await db.TiposCombustible.Where(x => x.Activo).Select(x => x.Id).FirstAsync();
        var tankResponse = await client.PostAsJsonAsync("api/gestion/tanques", new
        {
            id = 999997, codigo = "QA-MASS-T1", nombre = "QA tank", estacionId = stationId,
            tipoCombustibleId = fuelId, capacidadGalones = 100m, nivelCriticoGalones = 10m,
            existenciaActualGalones = 25m, notificacionBajoActiva = true, numeroEpisodioBajo = 99, activo = true
        });
        Assert.Equal(HttpStatusCode.Created, tankResponse.StatusCode);
        var tank = await tankResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tankId = tank.GetProperty("id").GetInt64();
        Assert.NotEqual(999997L, tankId);
        Assert.Equal(0m, tank.GetProperty("existenciaActualGalones").GetDecimal());
        Assert.False(tank.TryGetProperty("notificacionBajoActiva", out _));
        var persisted = await db.Tanques.SingleAsync(x => x.Id == tankId);
        Assert.Equal(0m, persisted.ExistenciaActualGalones);
        Assert.False(persisted.NotificacionBajoActiva);
        Assert.Equal(0, persisted.NumeroEpisodioBajo);
    }

    [Fact]
    public async Task Todas_las_rutas_MVC_estan_clasificadas_y_cumplen_auth_y_roles_para_los_seis_roles()
    {
        await qa.ResetAsync();
        var descriptors = qa.Factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();
        var registered = descriptors.SelectMany(d =>
        {
            var methods = d.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(x => x.HttpMethods)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            return methods.Select(method => (Key: Key(method, d.AttributeRouteInfo?.Template ?? ""), Descriptor: d));
        }).ToArray();

        Assert.Equal(registered.Length, registered.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(67, registered.Length);
        Assert.Equal(Matrix.Keys.Order(StringComparer.OrdinalIgnoreCase), registered.Select(x => x.Key).Order(StringComparer.OrdinalIgnoreCase));

        foreach (var item in registered)
        {
            var metadata = item.Descriptor.EndpointMetadata;
            var isPublic = metadata.OfType<IAllowAnonymous>().Any();
            var expected = Matrix[item.Key];
            Assert.Equal(expected is { Length: 0 }, isPublic);
            if (expected is { Length: > 0 })
            {
                var roleSets = metadata.OfType<IAuthorizeData>()
                    .Where(x => !string.IsNullOrWhiteSpace(x.Roles))
                    .Select(x => x.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
                var declaredRoles = roleSets.Length == 0 ? [] : roleSets.Skip(1).Aggregate(new HashSet<string>(roleSets[0], StringComparer.OrdinalIgnoreCase),
                    (intersection, next) => { intersection.IntersectWith(next); return intersection; }).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), declaredRoles);
            }
        }

        var identities = await CreateRoleUsersAsync();
        using var client = qa.Factory.CreateClient();
        var protectedRoutes = registered.Where(x => Matrix[x.Key] is not { Length: 0 }).ToArray();
        foreach (var route in protectedRoutes)
        {
            var routeTemplate = route.Descriptor.AttributeRouteInfo?.Template ?? "";
            client.DefaultRequestHeaders.Authorization = null;
            using var anonymous = await SendAsync(client, route.Key, routeTemplate, null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            foreach (var role in Roles)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identities[role]);
                using var response = await SendAsync(client, route.Key, routeTemplate, JsonContentFor(route.Key));
                var allowed = Matrix[route.Key] is null || Matrix[route.Key]!.Contains(role, StringComparer.OrdinalIgnoreCase);
                if (!allowed)
                {
                    Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                        $"{role} debe recibir 403 en {route.Key}, obtuvo {(int)response.StatusCode}.");
                }
                else
                {
                    Assert.True(response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && (int)response.StatusCode < 500,
                        $"{role} está autorizado en {route.Key}, obtuvo {(int)response.StatusCode}.");
                }
            }
        }
        client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<Dictionary<string, string>> CreateRoleUsersAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var scope = qa.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
            foreach (var role in Roles)
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                var hash = Rfc2898DeriveBytes.Pbkdf2(ApiTestFactory.TestPassword, salt, 100_000, HashAlgorithmName.SHA256, 32);
                var user = new Usuario
                {
                    NombreUsuario = $"qa.matrix.{role.ToLowerInvariant()}",
                    Correo = $"qa.matrix.{role.ToLowerInvariant()}@example.test",
                    NombreCompleto = $"QA {role}",
                    Activo = true,
                    PasswordHash = $"PBKDF2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"
                };
                db.Usuarios.Add(user);
                await db.SaveChangesAsync();
                var roleId = await db.Roles.Where(x => x.Nombre == role).Select(x => x.Id).SingleAsync();
                db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = user.Id, RolId = roleId });
            }
            await db.SaveChangesAsync();
        }
        using var client = qa.Factory.CreateClient();
        foreach (var role in Roles)
        {
            using var response = await client.PostAsJsonAsync("api/login", new
            {
                usuario = $"qa.matrix.{role.ToLowerInvariant()}",
                contrasena = ApiTestFactory.TestPassword
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            result[role] = payload.GetProperty("token").GetString()!;
        }
        return result;
    }

    private static JsonContent? JsonContentFor(string key) => key.StartsWith("GET ", StringComparison.OrdinalIgnoreCase) ? null : JsonContent.Create(new { });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string key, string routeTemplate, HttpContent? content)
    {
        var separator = key.IndexOf(' ');
        var method = key[..separator];
        var path = System.Text.RegularExpressions.Regex.Replace(routeTemplate, @"\{([^}:]+)(:[^}]+)?\}", match =>
        {
            var constraint = match.Groups[2].Value;
            return constraint.Equals(":guid", StringComparison.OrdinalIgnoreCase) ? Guid.Empty.ToString("D") :
                match.Groups[1].Value.Equals("tipo", StringComparison.OrdinalIgnoreCase) ? "departamentos" : "1";
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = content };
        return await client.SendAsync(request);
    }

    private static string Key(string method, string template)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(template.Trim('/'), @"\{([^}:]+)(:[^}]+)?\}", "{$1}");
        return $"{method.ToUpperInvariant()} /{normalized}".ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string[]?> BuildMatrix()
    {
        var result = new Dictionary<string, string[]?>(StringComparer.OrdinalIgnoreCase);
        void Add(string method, string path, string[]? roles) => result.Add(Key(method, path), roles);
        void Group(string method, string[]? roles, params string[] paths) { foreach (var path in paths) Add(method, path, roles); }

        Group("GET", AdminAuditor, "api/auditoria");
        Group("POST", [], "api/login", "api/login/registro", "api/login/inicializar-admin");
        Group("GET", null, "api/catalogos/departamentos", "api/catalogos/empleados", "api/catalogos/vehiculos", "api/catalogos/tipos-combustible", "api/catalogos/tanques", "api/catalogos/estaciones", "api/catalogos/roles");
        Group("GET", AdminSupervisorDispatcherAuditor, "api/cierres-diarios/resumen", "api/cierres-diarios", "api/cierres-diarios/{id}", "api/cierres-diarios/{id}/pdf");
        Group("POST", AdminSupervisorDispatcher, "api/cierres-diarios");
        Group("POST", ["ADMINISTRADOR", "DESPACHADOR"], "api/despachos");
        Group("POST", AdminSupervisor, "api/gestion/departamentos", "api/gestion/empleados", "api/gestion/vehiculos", "api/gestion/estaciones", "api/gestion/tanques");
        Group("PUT", AdminSupervisor, "api/gestion/departamentos/{id}", "api/gestion/empleados/{id}", "api/gestion/vehiculos/{id}");
        Group("DELETE", AdminSupervisor, "api/gestion/{tipo}/{id}");
        Group("GET", null, "api/inventario", "api/inventario/movimientos");
        Group("POST", AdminSupervisor, "api/inventario/ajustes");
        Group("GET", AdminSupervisorDispatcherAuditor, "api/notificaciones", "api/notificaciones/no-leidas");
        Group("POST", AdminSupervisorDispatcherAuditor, "api/notificaciones/{id}/leer", "api/notificaciones/leer-todas");
        Group("GET", AdminSupervisor, "api/programaciones", "api/programaciones/{id}", "api/programaciones/{id}/ejecuciones");
        Group("POST", AdminSupervisor, "api/programaciones", "api/programaciones/{id}/activar", "api/programaciones/{id}/desactivar");
        Group("PUT", AdminSupervisor, "api/programaciones/{id}");
        Group("GET", null, "api/recepciones/proveedores");
        Group("POST", AdminSupervisor, "api/recepciones/proveedores", "api/recepciones");
        Group("GET", null, "api/reportes", "api/reportes/exportar");
        Group("GET", null, "api/solicitudes", "api/solicitudes/{id}");
        Group("POST", ["ADMINISTRADOR", "SUPERVISOR", "SOLICITANTE"], "api/solicitudes");
        Group("PUT", AdminSupervisor, "api/solicitudes/{id}/aprobar", "api/solicitudes/{id}/rechazar");
        Group("POST", AdminSupervisor, "api/tickets/{id}/enviar", "api/tickets/{id}/reenviar", "api/tickets/{id}/envios/{envioId}/reconciliar");
        Group("GET", AdminSupervisor, "api/tickets/{id}/envios");
        Group("GET", [], "api/tickets/public/qr");
        Group("GET", null, "api/tickets", "api/tickets/{id}");
        Group("POST", AdminSupervisor, "api/tickets/{id}/anular", "api/tickets");
        Group("GET", AdminSupervisor, "api/tickets/{id}/qr");
        Group("POST", null, "api/tickets/validar");
        Group("GET", ["ADMINISTRADOR"], "api/gestion/usuarios");
        Group("POST", ["ADMINISTRADOR"], "api/gestion/usuarios", "api/gestion/usuarios/{id}/restablecer-contrasena", "api/gestion/usuarios/{id}/activar");
        Group("PUT", ["ADMINISTRADOR"], "api/gestion/usuarios/{id}");
        Group("DELETE", ["ADMINISTRADOR"], "api/gestion/usuarios/{id}");
        return result;
    }
}
