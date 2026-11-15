using Microsoft.AspNetCore.Mvc;
using ValidationSystem.Api.Contracts;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(ValidationService service) : ControllerBase
{
    /// <summary>Run summary: status, counts, duration, result columns and the conditions (for filtering by reason).</summary>
    [HttpGet("{runId:long}")]
    public async Task<RunDto> Get(long runId, CancellationToken ct) => (await service.GetRunAsync(runId, ct)).ToDto();

    /// <summary>Stored violating records with search (title, values, reasons) and filter by failed condition.</summary>
    [HttpGet("{runId:long}/results")]
    public async Task<PagedResultsDto> Results(
        long runId,
        [FromQuery] string? search,
        [FromQuery] string? conditionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new ResultQuery(search, conditionId, page, pageSize);
        var results = await service.GetResultsAsync(runId, query, ct);
        return new PagedResultsDto(results.TotalCount, Math.Max(1, page), Math.Clamp(pageSize, 1, 500),
                                   results.Items.Select(r => r.ToDto()).ToList());
    }
}
