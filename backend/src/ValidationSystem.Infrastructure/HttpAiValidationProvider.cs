using System.Net.Http.Json;
using System.Text.Json;
using ValidationSystem.Core.Ai;

namespace ValidationSystem.Infrastructure;

public sealed class AiServiceOptions
{
    /// <summary>Base URL of the AI service (the Python mock by default).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5005";
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Calls an external AI service over HTTP: POST {BaseUrl}/generate with { prompt, context }.
/// The Python mock implements this contract; a real provider could implement the same HTTP contract,
/// or a new <see cref="IAiValidationProvider"/> could call an LLM API directly.
/// </summary>
public sealed class HttpAiValidationProvider(HttpClient http) : IAiValidationProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Name => "python-mock";

    public async Task<string> GenerateAsync(AiRequest request, CancellationToken ct = default)
    {
        try
        {
            // Buffered body with Content-Length (not chunked): simple HTTP servers such as the Python mock require it.
            var body = JsonSerializer.Serialize(new { prompt = request.Prompt, context = request.Context }, Json);
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("generate", content, ct);
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"שירות ה-AI החזיר שגיאה ({(int)response.StatusCode})");
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException("לא ניתן להתחבר לשירות ה-AI", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiProviderException("שירות ה-AI לא הגיב בזמן", ex);
        }
    }
}
