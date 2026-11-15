using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ValidationSystem.Core.Ai;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dbOptions = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
        dbOptions.ConnectionString = configuration.GetConnectionString("ValidationDb")
            ?? throw new InvalidOperationException("Connection string 'ValidationDb' is not configured.");

        services.AddSingleton(dbOptions);
        services.AddSingleton<SqlConnectionFactory>();
        services.AddSingleton<IMetadataCatalogProvider, SqlMetadataCatalogProvider>();
        services.AddSingleton<IQueryExecutor, SqlQueryExecutor>();
        services.AddSingleton<IValidationRepository, SqlValidationRepository>();

        // AI provider: the HTTP client to the Python mock. Replace this registration to use a real provider.
        var aiOptions = configuration.GetSection("AiService").Get<AiServiceOptions>() ?? new AiServiceOptions();
        services.AddHttpClient<IAiValidationProvider, HttpAiValidationProvider>(client =>
        {
            client.BaseAddress = new Uri(aiOptions.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(aiOptions.TimeoutSeconds);
        });
        return services;
    }
}
