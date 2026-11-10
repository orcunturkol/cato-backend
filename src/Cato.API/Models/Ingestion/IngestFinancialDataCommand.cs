using Cato.API.DTOs;
using Cato.Domain.Entities;
using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>
/// A sales JSON file for one game. <paramref name="ReportPartnerId"/> is the Steamworks
/// partner the file belongs to: the upload replaces only that partner's lines.
/// </summary>
public record IngestFinancialDataCommand(int AppId, string FileName, Stream Content, int ReportPartnerId = SteamworksPartner.LegacyPartnerId)
    : IRequest<IngestionResult>;
