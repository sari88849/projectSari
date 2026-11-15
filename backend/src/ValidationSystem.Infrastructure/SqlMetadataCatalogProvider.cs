using Dapper;
using Microsoft.Extensions.Logging;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Infrastructure;

/// <summary>
/// Builds the approved catalog from TWO sources:
///   1. Business metadata (meta.Entities / meta.Fields / meta.Relationships) - labels, types, relationships.
///   2. Database metadata (sys.columns) - the real physical schema.
/// An item is exposed only if both agree (table/column exists and the SQL type matches the business type).
/// Mismatches are logged and skipped, so a configuration mistake can never produce a broken or unsafe query.
/// The catalog is cached in memory and reloaded after <see cref="Invalidate"/>.
/// </summary>
public sealed class SqlMetadataCatalogProvider(SqlConnectionFactory db, ILogger<SqlMetadataCatalogProvider> logger) : IMetadataCatalogProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile MetadataCatalog? _cached;

    public void Invalidate() => _cached = null;

    public async Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached) return cached;
        await _lock.WaitAsync(ct);
        try
        {
            return _cached ??= await LoadAsync(ct);
        }
        finally { _lock.Release(); }
    }

    private sealed record EntityRow(int EntityId, string EntityKey, string DisplayName, string DisplayNamePlural, string? Description,
                                    string SchemaName, string TableName, string PrimaryKeyColumn);
    private sealed record FieldRow(int EntityId, string FieldKey, string DisplayName, string? Description, string? Synonyms, string DataType,
                                   string? ColumnName, string? ComputedFunction, string? ComputedSourceFieldKey, string? TrueLabel,
                                   string? FalseLabel, bool IsMandatory, bool IsTitle, bool IsDefaultDisplay, int DisplayOrder);
    private sealed record RelationshipRow(int FromEntityId, string RelationshipKey, string DisplayName, string? Description,
                                          int ToEntityId, string FromColumn, string ToColumn);
    private sealed record ColumnRow(string SchemaName, string TableName, string ColumnName, string TypeName);

    private async Task<MetadataCatalog> LoadAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT EntityId, EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn
            FROM meta.Entities WHERE IsActive = 1;

            SELECT EntityId, FieldKey, DisplayName, Description, Synonyms, DataType, ColumnName, ComputedFunction,
                   ComputedSourceFieldKey, TrueLabel, FalseLabel, IsMandatory, IsTitle, IsDefaultDisplay, DisplayOrder
            FROM meta.Fields WHERE IsActive = 1;

            SELECT FromEntityId, RelationshipKey, DisplayName, Description, ToEntityId, FromColumn, ToColumn
            FROM meta.Relationships WHERE IsActive = 1;

            SELECT s.name AS SchemaName, o.name AS TableName, c.name AS ColumnName, t.name AS TypeName
            FROM sys.columns c
            JOIN sys.objects o ON o.object_id = c.object_id AND o.type IN ('U', 'V')
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.types   t ON t.user_type_id = c.user_type_id;
            """, cancellationToken: ct));

        var entityRows = (await multi.ReadAsync<EntityRow>()).ToList();
        var fieldRows = (await multi.ReadAsync<FieldRow>()).ToList();
        var relationshipRows = (await multi.ReadAsync<RelationshipRow>()).ToList();
        var physical = (await multi.ReadAsync<ColumnRow>())
            .ToDictionary(c => (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant()), c => c.TypeName);

        string? SqlType(EntityRow e, string? column) =>
            column is null ? null : physical.GetValueOrDefault((e.SchemaName.ToLowerInvariant(), e.TableName.ToLowerInvariant(), column.ToLowerInvariant()));

        // 1. Entities whose table and primary key exist.
        var validEntities = entityRows.Where(e =>
        {
            if (SqlType(e, e.PrimaryKeyColumn) is not null) return true;
            logger.LogWarning("Catalog: entity {Entity} skipped - {Schema}.{Table}.{Pk} not found in database", e.EntityKey, e.SchemaName, e.TableName, e.PrimaryKeyColumn);
            return false;
        }).ToDictionary(e => e.EntityId);

        // 2. Fields: physical columns must exist with a compatible SQL type; computed fields need a valid source.
        var fieldsByEntity = new Dictionary<int, List<FieldMetadata>>();
        foreach (var e in validEntities.Values)
        {
            var rows = fieldRows.Where(f => f.EntityId == e.EntityId).ToList();
            var fields = new List<FieldMetadata>();

            foreach (var f in rows.Where(f => f.ColumnName is not null))
            {
                if (!Enum.TryParse<FieldDataType>(f.DataType, out var type)) { Skip(e, f, "unknown data type"); continue; }
                var sqlType = SqlType(e, f.ColumnName);
                if (sqlType is null) { Skip(e, f, "column not found in database"); continue; }
                if (!IsCompatible(type, sqlType)) { Skip(e, f, $"SQL type '{sqlType}' does not match {type}"); continue; }
                fields.Add(ToField(f, type, null));
            }
            foreach (var f in rows.Where(f => f.ColumnName is null))
            {
                if (!Enum.TryParse<FieldDataType>(f.DataType, out var type) ||
                    !Enum.TryParse<ComputedFunction>(f.ComputedFunction, out var function)) { Skip(e, f, "unknown computed function or type"); continue; }
                var source = fields.FirstOrDefault(x => string.Equals(x.Key, f.ComputedSourceFieldKey, StringComparison.OrdinalIgnoreCase));
                if (source is not { DataType: FieldDataType.Date }) { Skip(e, f, "computed source must be an existing Date field"); continue; }
                fields.Add(ToField(f, type, function));
            }
            fieldsByEntity[e.EntityId] = fields;
        }

        // 3. Relationships between valid entities whose join columns exist.
        var relationshipsByEntity = validEntities.Keys.ToDictionary(id => id, _ => new List<RelationshipMetadata>());
        foreach (var r in relationshipRows)
        {
            if (!validEntities.TryGetValue(r.FromEntityId, out var from) || !validEntities.TryGetValue(r.ToEntityId, out var to) ||
                SqlType(from, r.FromColumn) is null || SqlType(to, r.ToColumn) is null)
            {
                logger.LogWarning("Catalog: relationship {Key} skipped - entity or join column missing", r.RelationshipKey);
                continue;
            }
            relationshipsByEntity[r.FromEntityId].Add(new RelationshipMetadata(r.RelationshipKey, r.DisplayName, to.EntityKey, r.FromColumn, r.ToColumn, r.Description));
        }

        var catalog = new MetadataCatalog(validEntities.Values.Select(e => new EntityMetadata(
            e.EntityKey, e.DisplayName, e.DisplayNamePlural, e.Description, e.SchemaName, e.TableName, e.PrimaryKeyColumn,
            fieldsByEntity[e.EntityId], relationshipsByEntity[e.EntityId])));

        logger.LogInformation("Metadata catalog loaded: {Entities} entities, {Fields} fields",
            catalog.Entities.Count, catalog.Entities.Sum(e => e.Fields.Count));
        return catalog;

        void Skip(EntityRow e, FieldRow f, string reason) =>
            logger.LogWarning("Catalog: field {Entity}.{Field} skipped - {Reason}", e.EntityKey, f.FieldKey, reason);
    }

    private static FieldMetadata ToField(FieldRow f, FieldDataType type, ComputedFunction? function) => new(
        f.FieldKey, f.DisplayName, type, f.ColumnName, function, f.ComputedSourceFieldKey, f.Description, f.Synonyms,
        f.TrueLabel, f.FalseLabel, f.IsMandatory, f.IsTitle, f.IsDefaultDisplay, f.DisplayOrder);

    private static bool IsCompatible(FieldDataType type, string sqlType) => type switch
    {
        FieldDataType.String => sqlType is "nvarchar" or "varchar" or "nchar" or "char",
        FieldDataType.Number => sqlType is "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "money" or "float" or "real",
        FieldDataType.Date => sqlType is "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset",
        FieldDataType.Boolean => sqlType is "bit",
        _ => false
    };
}
