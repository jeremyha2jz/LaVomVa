namespace TicketsCombustible.Api.Common;

/// <summary>Formato único para las respuestas de error de la API.</summary>
public sealed record ApiErrorResponse(string Mensaje, object? Errores = null);
