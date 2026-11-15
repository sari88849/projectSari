using Microsoft.AspNetCore.Mvc;
using ValidationSystem.Core.Ai;

namespace ValidationSystem.Api.Controllers;

public sealed record AiProposalRequest(string Prompt, string? RootEntity);

/// <summary>
/// Natural-language validation proposals. Returns a proposal only - saving (and then executing)
/// requires the user's explicit approval through the regular validations endpoints.
/// </summary>
[ApiController]
[Route("api/ai")]
public sealed class AiController(AiProposalService service) : ControllerBase
{
    /// <summary>The metadata context sent to the AI provider (transparency / debugging).</summary>
    [HttpGet("context")]
    public Task<AiContext> Context([FromQuery] string? rootEntity, CancellationToken ct) => service.GetContextAsync(rootEntity, ct);

    [HttpPost("proposals")]
    public Task<AiProposal> Propose(AiProposalRequest request, CancellationToken ct) =>
        service.ProposeAsync(request.Prompt ?? "", request.RootEntity, ct);
}
