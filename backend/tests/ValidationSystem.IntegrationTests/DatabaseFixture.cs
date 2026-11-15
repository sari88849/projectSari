using Microsoft.Extensions.Logging.Abstractions;
using ValidationSystem.Core.Services;
using ValidationSystem.Infrastructure;

namespace ValidationSystem.IntegrationTests;

/// <summary>
/// Wires the real engine against the real SQL Server database (ValidationSystemDb, seeded by db/setup-database.ps1).
/// Override the connection with the VALIDATION_TEST_DB environment variable.
/// </summary>
public sealed class DatabaseFixture
{
    public const string DefaultConnectionString =
        "Server=.;Database=ValidationSystemDb;Trusted_Connection=True;TrustServerCertificate=True;Application Name=ValidationSystem.Tests";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("VALIDATION_TEST_DB") ?? DefaultConnectionString;

    public SqlConnectionFactory Db { get; }
    public SqlMetadataCatalogProvider Catalog { get; }
    public SqlQueryExecutor Executor { get; }
    public SqlValidationRepository Repository { get; }
    public ValidationService Service { get; }

    public DatabaseFixture()
    {
        Db = new SqlConnectionFactory(new DatabaseOptions { ConnectionString = ConnectionString });
        Catalog = new SqlMetadataCatalogProvider(Db, NullLogger<SqlMetadataCatalogProvider>.Instance);
        Executor = new SqlQueryExecutor(Db);
        Repository = new SqlValidationRepository(Db);
        Service = new ValidationService(Catalog, Repository, Executor, new ValidationOptions(), NullLogger<ValidationService>.Instance);
    }

    public async Task<int> ValidationIdByNameAsync(string name) =>
        (await Repository.ListAsync(null, null)).Single(v => v.Name == name).Id;
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
