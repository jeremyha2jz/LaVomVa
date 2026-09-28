using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.Text.Json.Serialization;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Services;
using TicketsCombustible.Api.Hubs;
using TicketsCombustible.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TicketsCombustible")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:TicketsCombustible.");
SecurityConfiguration.Validate(builder.Configuration, builder.Environment);

builder.Services.AddDbContext<TicketsCombustibleDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditoriaService, AuditoriaService>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<CierreDiarioService>();
builder.Services.AddScoped<ReportesService>();
builder.Services.AddScoped<SolicitudProgramacionService>();
builder.Services.AddScoped<ISolicitudProgramacionProcessor>(services => services.GetRequiredService<SolicitudProgramacionService>());
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<SolicitudProgramacionWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TicketLifecycleService>();
builder.Services.AddScoped<NotificacionService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SignalRInventoryEventSink>();
builder.Services.AddSingleton<IInventoryEventSink>(services => services.GetRequiredService<SignalRInventoryEventSink>());
builder.Services.AddScoped<IInventoryRealtimePublisher, InventoryRealtimePublisher>();
builder.Services.AddScoped<TicketNotificationProcessor>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<ISmsSender, HttpSmsSender>();
builder.Services.AddScoped<TicketDeliveryService>();
builder.Services.AddMemoryCache();
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TicketsCombustible.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LaVomVa.Client";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/inventory"))
                context.Token = accessToken;
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            if (!long.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) { context.Fail("Sesión inválida."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<TicketsCombustibleDbContext>();
            var active = await db.Usuarios.AnyAsync(x => x.Id == userId && x.Activo);
            if (!active) { context.Fail("Cuenta desactivada."); return; }
            var currentRoles = await (from ur in db.UsuarioRoles join role in db.Roles on ur.RolId equals role.Id
                where ur.UsuarioId == userId && role.Activo select role.Nombre).ToListAsync();
            if (!currentRoles.Order().SequenceEqual((context.Principal?.FindAll(ClaimTypes.Role).Select(x => x.Value) ?? []).Order()))
                context.Fail("Los roles de la cuenta cambiaron; inicia sesión de nuevo.");
        }
    };
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("pwa", policy =>
{
    if (builder.Environment.IsDevelopment()) policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));
var trustedProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in trustedProxies)
        {
            if (!System.Net.IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException("ReverseProxy:KnownProxies solo admite direcciones IP explícitas.");
            options.KnownProxies.Add(address);
        }
    });
}
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new IpAddressJsonConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var errores = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "El valor enviado no es válido." : error.ErrorMessage).ToArray());
        return new BadRequestObjectResult(new ApiErrorResponse("La solicitud contiene datos inválidos.", errores));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpsRedirection(options =>
{
    if (builder.Configuration.GetValue<int?>("HttpsRedirection:HttpsPort") is { } httpsPort)
        options.HttpsPort = httpsPort;
});

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiErrorResponse("Ocurrió un error interno al procesar la solicitud."));
}));
// Va antes de autenticación/autorización para que sus 401/403 también salgan como ApiErrorResponse.
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    var mensaje = response.StatusCode switch
    {
        StatusCodes.Status404NotFound => "No se encontró el recurso solicitado.",
        StatusCodes.Status401Unauthorized => "No está autorizado para realizar esta acción.",
        StatusCodes.Status403Forbidden => "No tiene permisos para realizar esta acción.",
        _ => "La solicitud no pudo procesarse."
    };
    await response.WriteAsJsonAsync(new ApiErrorResponse(mensaje));
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    if (trustedProxies.Length > 0) app.UseForwardedHeaders();
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("pwa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<InventoryHub>("/hubs/inventory");
app.Run();

public partial class Program { }
