using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

/// <summary>
/// Sets the Steam main capsule (616x353) on the given games without a full enrichment.
/// Rows enriched before 2026-11-10 hold the 231x87 small capsule until their next refresh;
/// a sibling service (OGT) sends the app ids it shows.
/// </summary>
public record RefreshMainCapsulesCommand(List<int> AppIds) : IRequest<Result<RefreshMainCapsulesResult>>
{
    public const int MaxAppIds = 5000;
}

/// <param name="Requested">Distinct app ids in the request.</param>
/// <param name="Known">Of those, the ones CATO has a row for.</param>
/// <param name="Updated">Rows whose capsule URL changed.</param>
/// <param name="FailedBatches">Steam calls that failed; their games keep the old URL.</param>
public record RefreshMainCapsulesResult(int Requested, int Known, int Updated, int FailedBatches);
