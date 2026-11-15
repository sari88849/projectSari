using Microsoft.AspNetCore.Mvc;
using ValidationSystem.Api.Contracts;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Api.Controllers;

[ApiController]
[Route("api/validations")]
public sealed class ValidationsController(ValidationService service) : ControllerBase
{
    /// <summary>Saved validations with their latest run status, optionally filtered by text and entity.</summary>
    [HttpGet]
    public async Task<IEnumerable<ValidationSummary>> List([FromQuery] string? search, [FromQuery] string? entity, CancellationToken ct) =>
        (await service.ListAsync(search, entity, ct)).Select(v => v.ToDto());

    [HttpGet("{id:int}")]
    public async Task<ValidationDto> Get(int id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToDto();

    [HttpPost]
    public async Task<ActionResult<ValidationDto>> Create(SaveValidationRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(request.Name ?? "", request.Description, request.Definition,
                                                request.Source ?? ValidationSource.Manual, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ValidationDto> Update(int id, SaveValidationRequest request, CancellationToken ct) =>
        (await service.UpdateAsync(id, request.Name ?? "", request.Description, request.Definition, ct)).ToDto();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Validates a definition and returns its Hebrew description and generated SQL, without executing it.</summary>
    [HttpPost("preview")]
    public Task<DefinitionPreview> Preview(PreviewRequest request, CancellationToken ct) => service.PreviewAsync(request.Definition, ct);

    /// <summary>Executes the validation now. A failed run is returned with status "Failed" and a business-level error.</summary>
    [HttpPost("{id:int}/execute")]
    public async Task<RunDto> Execute(int id, CancellationToken ct) => (await service.ExecuteAsync(id, ct)).ToDto();

    [HttpGet("{id:int}/runs")]
    public async Task<IEnumerable<RunDto>> Runs(int id, CancellationToken ct) =>
        (await service.ListRunsAsync(id, ct)).Select(r => r.ToDto());
}
