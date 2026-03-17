using MediatR;

namespace Cato.API.Models.SteamDb;

public record GetAvailableDatesQuery(string Source) : IRequest<List<DateOnly>>;
