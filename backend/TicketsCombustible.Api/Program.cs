using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.Text.Json.Serialization;
using TicketsCombustible.Api.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TicketsCombustible")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:TicketsCombustible.");

builder.Services.AddDbContext<TicketsCombustibleDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddMemoryCache();
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Falta Jwt:Key.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32 || jwtKey.StartsWith("REEMPLAZA_")) throw new InvalidOperationException("Configura Jwt:Key con al menos 32 bytes privados.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true
    };
    options.Events = new JwtBearerEvents
    {
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
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseCors("pwa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
