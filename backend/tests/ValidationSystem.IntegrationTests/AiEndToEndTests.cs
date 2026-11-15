using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ValidationSystem.IntegrationTests;

/// <summary>Starts the real Python mock service (ai-mock/server.py) on a free port.</summary>
public sealed class PythonMockFixture : IAsyncLifetime
{
    private Process? _process;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ai-mock", "server.py"));
        var python = Environment.GetEnvironmentVariable("PYTHON") ?? "python";

        _process = Process.Start(new ProcessStartInfo(python, $"\"{script}\" --port {port}")
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script)!
        }) ?? throw new InvalidOperationException("Could not start Python.");

        using var http = new HttpClient();
        for (var i = 0; i < 50; i++)
        {
            try
            {
                if ((await http.GetAsync(BaseUrl + "/health")).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        throw new InvalidOperationException("Python AI mock did not start: " + await _process.StandardError.ReadToEndAsync());
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        _process?.Dispose();
        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// Full AI flow: Angular-equivalent HTTP calls -> .NET API -> Python mock -> translation -> validation ->
/// user approval (save) -> execution with the same engine as manually built validations.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AiEndToEndTests(PythonMockFixture python) : IClassFixture<PythonMockFixture>
{
    private HttpClient Client() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("ConnectionStrings:ValidationDb", DatabaseFixture.ConnectionString);
        b.UseSetting("AiService:BaseUrl", python.BaseUrl);
    }).CreateClient();

    [Theory]
    [InlineData("עובדים מתחת לגיל 18 עם שכר חודשי מעל 15000", new[] { "13", "14" })]
    [InlineData("עובדים שתאריך סיום העסקה מוקדם מתאריך תחילת העסקה", new[] { "11", "12" })]
    [InlineData("עובדים פעילים במחלקות לא פעילות", new[] { "18", "19" })]
    [InlineData("עובדים שחסר להם תעודת זהות או דואר אלקטרוני או שיוך למחלקה או תאריך לידה", new[] { "19", "21", "22", "23", "24", "25" })]
    [InlineData("עובדים עם שכר חודשי גבוה משכר מקסימלי לתפקיד", new[] { "9", "13", "14", "16" })]
    public async Task Natural_language_request_produces_same_results_as_manual_validation(string prompt, string[] expectedKeys)
    {
        var client = Client();

        // 1. Proposal (nothing saved yet)
        var proposalResponse = await client.PostAsJsonAsync("/api/ai/proposals", new { prompt });
        Assert.Equal(HttpStatusCode.OK, proposalResponse.StatusCode);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(proposal.GetProperty("isValid").GetBoolean(), proposal.ToString());
        Assert.Equal("python-mock", proposal.GetProperty("provider").GetString());
        Assert.False(string.IsNullOrEmpty(proposal.GetProperty("ruleText").GetString()));

        // 2. User approves -> saved as a regular validation (source AI)
        var save = await client.PostAsJsonAsync("/api/validations", new
        {
            name = "AI: " + prompt,
            description = proposal.GetProperty("description").GetString(),
            definition = proposal.GetProperty("definition"),
            source = "AI"
        });
        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        var id = (await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        try
        {
            // 3. Executed by the generic engine
            var run = await (await client.PostAsync($"/api/validations/{id}/execute", null)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Succeeded", run.GetProperty("status").GetString());
            var results = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{run.GetProperty("runId").GetInt64()}/results?pageSize=500");
            Assert.Equal(expectedKeys, results.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("recordKey").GetString()));
        }
        finally
        {
            await client.DeleteAsync($"/api/validations/{id}");
        }
    }

    [Fact]
    public async Task Unsupported_request_returns_invalid_proposal_with_explanation()
    {
        var proposal = await (await Client().PostAsJsonAsync("/api/ai/proposals", new { prompt = "מה מזג האוויר מחר" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(proposal.GetProperty("isValid").GetBoolean());
        Assert.True(proposal.GetProperty("errors").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Null, proposal.GetProperty("definition").ValueKind);
    }

    [Fact]
    public async Task Unavailable_ai_service_returns_503()
    {
        var client = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:ValidationDb", DatabaseFixture.ConnectionString);
            b.UseSetting("AiService:BaseUrl", "http://127.0.0.1:9");
        }).CreateClient();

        var response = await client.PostAsJsonAsync("/api/ai/proposals", new { prompt = "עובדים מתחת לגיל 18" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
