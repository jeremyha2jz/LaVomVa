using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using TicketsCombustible.Api.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TicketsCombustible")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:TicketsCombustible.");

builder.Services.AddDbContext<TicketsCombustibleDbContext>(options => options.UseNpgsql(connectionString));
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Falta Jwt:Key.");
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
});
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("pwa", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
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

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiErrorResponse("Ocurrió un error interno al procesar la solicitud."));
}));
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("pwa");
app.UseAuthentication();
app.UseAuthorization();
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
app.MapControllers();
app.Run();
