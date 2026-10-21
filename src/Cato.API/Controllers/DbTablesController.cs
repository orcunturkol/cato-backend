using System.Text;
using Cato.API.Models.DbTables;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/db-tables")]
[Tags("Database Tables")]
public class DbTablesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DbTablesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Every table: live row estimate, data sources, validation and fallback.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DbTablesDto), StatusCodes.Status200OK)]
    public async Task<IResult> List(CancellationToken ct) =>
        Results.Ok(await _mediator.Send(new GetDbTablesQuery(), ct));

    /// <summary>Live row estimates by table (Postgres statistics, no table scan).</summary>
    [HttpGet("counts")]
    [ProducesResponseType(typeof(DbTableCountsDto), StatusCodes.Status200OK)]
    public async Task<IResult> Counts(CancellationToken ct) =>
        Results.Ok(await _mediator.Send(new GetDbTableCountsQuery(), ct));

    /// <summary>All tables as CSV in the layout of the "DB-Tables&amp;Content" sheet, with exact counts.</summary>
    [HttpGet("export.csv")]
    [Produces("text/csv")]
    public async Task<IResult> ExportCsv(CancellationToken ct)
    {
        var csv = await _mediator.Send(new ExportDbTablesCsvQuery(), ct);
        // The BOM makes Excel read the file as UTF-8; Google Sheets ignores it.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", "cato-db-tables.csv");
    }

    /// <summary>One table: columns with their source, validation and fallback, plus an exact row count (cached 5 min).</summary>
    [HttpGet("{table}")]
    [ProducesResponseType(typeof(DbTableDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> Get(string table, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDbTableQuery(table), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
