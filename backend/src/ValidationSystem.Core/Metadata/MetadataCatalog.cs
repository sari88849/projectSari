using System.Text.RegularExpressions;

namespace ValidationSystem.Core.Metadata;

/// <summary>
/// The approved metadata catalog: the ONLY source of entities, fields and relationships
/// the engine may use. Every identifier is checked when the catalog is built, so nothing
/// that reaches SQL generation can contain anything other than a plain identifier.
/// Keys are matched case-insensitively and normalized to their catalog spelling.
/// </summary>
public sealed partial class MetadataCatalog
{
    public const int MaxRelationshipHops = 3;

    private readonly Dictionary<string, EntityMetadata> _entities;

    public MetadataCatalog(IEnumerable<EntityMetadata> entities)
    {
        _entities = new Dictionary<string, EntityMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (!_entities.TryAdd(entity.Key, entity))
                throw new InvalidOperationException($"Duplicate entity key '{entity.Key}'.");
        }
        EnsureConsistent();
    }

    public IReadOnlyCollection<EntityMetadata> Entities => _entities.Values;

    public EntityMetadata? FindEntity(string? key) =>
        key is not null && _entities.TryGetValue(key, out var e) ? e : null;

    public static FieldMetadata? FindField(EntityMetadata entity, string key) =>
        entity.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public static RelationshipMetadata? FindRelationship(EntityMetadata entity, string key) =>
        entity.Relationships.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves "Rel1.Rel2.Field" starting at <paramref name="rootEntityKey"/>.
    /// Returns null and an error message (Hebrew, business-level) when the path is not in the catalog.
    /// </summary>
    public ResolvedField? ResolveField(string rootEntityKey, string? path, out string? error)
    {
        error = null;
        var root = FindEntity(rootEntityKey);
        if (root is null) { error = $"הישות '{rootEntityKey}' אינה קיימת בקטלוג"; return null; }
        if (string.IsNullOrWhiteSpace(path)) { error = "לא נבחר שדה"; return null; }

        var parts = path.Split('.');
        if (parts.Length - 1 > MaxRelationshipHops)
        {
            error = $"נתיב השדה '{path}' ארוך מדי (מותרים עד {MaxRelationshipHops} קשרים)";
            return null;
        }

        var current = root;
        var hops = new List<RelationshipMetadata>();
        var canonical = new List<string>();
        var labels = new List<string>();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var rel = FindRelationship(current, parts[i]);
            if (rel is null)
            {
                error = $"הקשר '{parts[i]}' אינו מוגדר עבור הישות '{current.DisplayName}'";
                return null;
            }
            hops.Add(rel);
            canonical.Add(rel.Key);
            labels.Add(rel.DisplayName);
            current = _entities[rel.TargetEntityKey];
        }

        var field = FindField(current, parts[^1]);
        if (field is null)
        {
            error = $"השדה '{parts[^1]}' אינו מוגדר עבור הישות '{current.DisplayName}'";
            return null;
        }
        canonical.Add(field.Key);
        labels.Add(field.DisplayName);

        return new ResolvedField(string.Join('.', canonical), hops, current, field, string.Join(" › ", labels));
    }

    /// <summary>Resolves a path known to be valid (throws otherwise).</summary>
    public ResolvedField Resolve(string rootEntityKey, string path) =>
        ResolveField(rootEntityKey, path, out var error) ?? throw new InvalidOperationException(error);

    /// <summary>
    /// All field paths reachable from an entity up to <paramref name="maxHops"/> relationships,
    /// for the builder's field picker. Computed in-memory from the catalog.
    /// </summary>
    public IReadOnlyList<ResolvedField> GetReachableFields(string rootEntityKey, int maxHops = 2)
    {
        var result = new List<ResolvedField>();
        var root = FindEntity(rootEntityKey);
        if (root is null) return result;
        Collect(root, [], maxHops);
        return result;

        void Collect(EntityMetadata entity, List<RelationshipMetadata> hops, int remaining)
        {
            var prefix = string.Concat(hops.Select(h => h.Key + "."));
            var labelPrefix = string.Concat(hops.Select(h => h.DisplayName + " › "));
            foreach (var f in entity.Fields.OrderBy(f => f.DisplayOrder))
                result.Add(new ResolvedField(prefix + f.Key, hops.ToList(), entity, f, labelPrefix + f.DisplayName));
            if (remaining == 0) return;
            foreach (var rel in entity.Relationships)
                Collect(_entities[rel.TargetEntityKey], [.. hops, rel], remaining - 1);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex IdentifierRegex();

    public static bool IsSafeIdentifier(string? value) => value is not null && IdentifierRegex().IsMatch(value);

    /// <summary>Rejects a catalog with unsafe identifiers or dangling references (defense in depth).</summary>
    private void EnsureConsistent()
    {
        foreach (var e in _entities.Values)
        {
            Require(e.Key, $"entity key '{e.Key}'");
            Require(e.SchemaName, $"schema of {e.Key}");
            Require(e.TableName, $"table of {e.Key}");
            Require(e.PrimaryKeyColumn, $"primary key of {e.Key}");

            foreach (var f in e.Fields)
            {
                Require(f.Key, $"field key {e.Key}.{f.Key}");
                if (f.IsComputed)
                {
                    var source = FindField(e, f.FunctionSourceFieldKey ?? "");
                    if (source is null || source.IsComputed || source.DataType != FieldDataType.Date)
                        throw new InvalidOperationException($"Computed field {e.Key}.{f.Key} must reference a physical Date field.");
                }
                else
                {
                    Require(f.ColumnName, $"column of {e.Key}.{f.Key}");
                }
            }

            foreach (var r in e.Relationships)
            {
                Require(r.Key, $"relationship key {e.Key}.{r.Key}");
                Require(r.FromColumn, $"from-column of {e.Key}.{r.Key}");
                Require(r.ToColumn, $"to-column of {e.Key}.{r.Key}");
                if (!_entities.ContainsKey(r.TargetEntityKey))
                    throw new InvalidOperationException($"Relationship {e.Key}.{r.Key} targets unknown entity '{r.TargetEntityKey}'.");
                if (FindField(e, r.Key) is not null)
                    throw new InvalidOperationException($"Relationship key {e.Key}.{r.Key} collides with a field key.");
            }
        }

        static void Require(string? identifier, string what)
        {
            if (!IsSafeIdentifier(identifier))
                throw new InvalidOperationException($"Unsafe or missing identifier for {what}: '{identifier}'.");
        }
    }
}
