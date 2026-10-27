using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Redis;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class SetReviewTotalListHandler : IRequestHandler<SetReviewTotalListCommand, ReviewTotalListDto>
{
    private readonly IReviewTotalList _list;

    public SetReviewTotalListHandler(IReviewTotalList list) => _list = list;

    public async Task<ReviewTotalListDto> Handle(SetReviewTotalListCommand request, CancellationToken ct) =>
        new(await _list.ReplaceAsync(request.AppIds.Distinct().ToList(), ct));
}
