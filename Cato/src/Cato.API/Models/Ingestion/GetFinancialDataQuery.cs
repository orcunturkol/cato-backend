using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetFinancialDataQuery(int AppId, string? CountryCode, int Limit = 100) : IRequest<List<FinancialDto>>;

public record FinancialDto(
    Guid Id,
    DateOnly SaleDate,
    string? CountryCode,
    int SalesUnits,
    int ReturnsUnits,
    int NetUnits,
    decimal GrossRevenueUsd,
    decimal NetRevenueUsd,
    string? SaleType,
    string? Platform);
