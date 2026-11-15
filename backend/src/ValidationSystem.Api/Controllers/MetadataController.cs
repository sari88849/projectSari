using Microsoft.AspNetCore.Mvc;
using ValidationSystem.Api.Contracts;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Api.Controllers;

/// <summary>Business metadata for the builder: entities, reachable fields (Hebrew labels) and operators.</summary>
[ApiController]
[Route("api/metadata")]
public sealed class MetadataController(IMetadataCatalogProvider catalogProvider) : ControllerBase
{
    [HttpGet("entities")]
    public async Task<IEnumerable<EntityDto>> GetEntities(CancellationToken ct)
    {
        var catalog = await catalogProvider.GetCatalogAsync(ct);
        return catalog.Entities.OrderBy(e => e.DisplayName).Select(e => e.ToDto());
    }

    /// <summary>Fields of an entity, including fields of related entities up to <paramref name="maxHops"/> relationships.</summary>
    [HttpGet("entities/{entityKey}/fields")]
    public async Task<ActionResult<IEnumerable<FieldDto>>> GetFields(string entityKey, [FromQuery] int maxHops = 2, CancellationToken ct = default)
    {
        var catalog = await catalogProvider.GetCatalogAsync(ct);
        if (catalog.FindEntity(entityKey) is null) throw new NotFoundException($"הישות '{entityKey}' אינה קיימת");
        return Ok(catalog.GetReachableFields(entityKey, Math.Clamp(maxHops, 0, 2)).Select(f => f.ToDto()));
    }

    [HttpGet("operators")]
    public IEnumerable<OperatorDto> GetOperators() => OperatorCatalog.Operators.Select(o => o.ToDto());

    /// <summary>Reloads the catalog after metadata configuration changes.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        catalogProvider.Invalidate();
        var catalog = await catalogProvider.GetCatalogAsync(ct);
        return Ok(new { entities = catalog.Entities.Count, fields = catalog.Entities.Sum(e => e.Fields.Count) });
    }
}
