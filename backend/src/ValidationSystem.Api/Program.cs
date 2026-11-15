using System.Text.Json.Serialization;
using ValidationSystem.Api;
using ValidationSystem.Core.Services;
using ValidationSystem.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// Engine + persistence
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(builder.Configuration.GetSection("Validation").Get<ValidationOptions>() ?? new ValidationOptions());
builder.Services.AddScoped<ValidationService>();
builder.Services.AddScoped<ValidationSystem.Core.Ai.AiProposalService>();

// Angular dev server
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) app.MapOpenApi();   // /openapi/v1.json
app.UseCors();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
