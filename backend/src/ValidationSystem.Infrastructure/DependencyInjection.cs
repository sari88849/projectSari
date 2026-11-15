using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
