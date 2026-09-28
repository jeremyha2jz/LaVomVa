using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TicketsCombustible.Api.Hubs;

[Authorize]
public sealed class InventoryHub : Hub { }
