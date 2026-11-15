namespace ValidationSystem.Core.Metadata;

/// <summary>Business data type of a field. Drives allowed operators, value parsing and SQL parameter types.</summary>
public enum FieldDataType { String, Number, Date, Boolean }

/// <summary>
/// Whitelisted computed-field functions. Metadata refers to them by name only;
/// the SQL for each one is fixed in <see cref="Sql.SqlQueryBuilder"/>.
/// </summary>
public enum ComputedFunction
{
    /// <summary>Full years between the source date and today (e.g. age, seniority).</summary>
    FullYearsSince,
    /// <summary>Days between the source date and today.</summary>
    DaysSince
}

/// <summary>A business entity (a table exposed to business users).</summary>
public sealed record EntityMetadata(
    string Key,
    string DisplayName,
    string DisplayNamePlural,
    string? Description,
    string SchemaName,
    string TableName,
    string PrimaryKeyColumn,
    IReadOnlyList<FieldMetadata> Fields,
    IReadOnlyList<RelationshipMetadata> Relationships);

/// <summary>A business field: either a physical column or a whitelisted computed function over another field.</summary>
public sealed record FieldMetadata(
    string Key,
    string DisplayName,
    FieldDataType DataType,
    string? ColumnName,
    ComputedFunction? Function = null,
    string? FunctionSourceFieldKey = null,
    string? Description = null,
    string? Synonyms = null,
    string? TrueLabel = null,
    string? FalseLabel = null,
    bool IsMandatory = false,
    bool IsTitle = false,
    bool IsDefaultDisplay = false,
    int DisplayOrder = 0)
{
    public bool IsComputed => Function is not null;
}

/// <summary>A navigable many-to-one relationship: owner.FromColumn = target.ToColumn.</summary>
public sealed record RelationshipMetadata(
    string Key,
    string DisplayName,
    string TargetEntityKey,
    string FromColumn,
    string ToColumn,
    string? Description = null);

/// <summary>
/// A field path (e.g. "Department.Manager.IsActive") resolved against the catalog:
/// the relationship hops from the root entity, and the final field.
/// </summary>
public sealed record ResolvedField(
    string Path,
    IReadOnlyList<RelationshipMetadata> Hops,
    EntityMetadata OwnerEntity,
    FieldMetadata Field,
    string Label)
{
    /// <summary>Path of the relationship part only ("" for root fields), used as the join key.</summary>
    public string RelationshipPath => string.Join('.', Hops.Select(h => h.Key));
}
