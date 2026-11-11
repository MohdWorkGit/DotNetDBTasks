using Jadwal.Domain.Enums;

namespace Jadwal.Application.Common.Interfaces;

/// <summary>
/// Reads what a connection's account can see — its tables and views, their columns and keys —
/// from the database's own catalog, so the AI assistant can write SQL against real names.
///
/// <para>Structure only, unless samples are asked for: then a few example values per text column
/// from a five-row read (System Settings, "ai.sampleValues"). On the default connection, which is
/// Jadwal's own database, Jadwal's own tables are never listed.</para>
/// </summary>
public interface ISchemaReader
{
    /// <summary>Every table and view the account can read, the connection's own schema first.</summary>
    Task<IReadOnlyList<SchemaObject>> ListObjectsAsync(SchemaTarget target, CancellationToken cancellationToken);

    /// <summary>Columns, primary key, foreign keys and (optionally) sample values of each object.</summary>
    Task<IReadOnlyList<SchemaObjectDetail>> DescribeAsync(
        SchemaTarget target, IReadOnlyList<SchemaObject> objects, bool includeSamples, CancellationToken cancellationToken);
}

/// <summary>Where to read: a named connection, or the default one (<c>DatabaseUserId</c> null).</summary>
public sealed record SchemaTarget(Guid? DatabaseUserId, string? ConnectionString, DatabaseServerType ServerType)
{
    public bool IsDefaultConnection => DatabaseUserId is null;
}

/// <param name="Schema">The owner or schema, as the catalog spells it.</param>
/// <param name="Kind"><c>table</c> or <c>view</c>.</param>
/// <param name="QualifiedName">How SQL refers to it: the schema prefix is left off for the connection's own schema.</param>
public sealed record SchemaObject(string Schema, string Name, string Kind, string QualifiedName);

public sealed record SchemaColumn(string Name, string DataType, bool Nullable);

public sealed record SchemaForeignKey(IReadOnlyList<string> Columns, string ReferencedObject, IReadOnlyList<string> ReferencedColumns);

public sealed record SchemaObjectDetail(
    SchemaObject Object,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<SchemaForeignKey> ForeignKeys,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Samples);

/// <summary>
/// The object list per connection, kept for a while: reading the catalog of a large database
/// takes seconds, and the admin opens the AI dialog more than once while writing a query.
/// </summary>
public interface ISchemaCache
{
    /// <param name="connectionKey">The connection's id, or <see cref="Guid.Empty"/> for the default one.</param>
    /// <param name="refresh">Read the catalog again even when a fresh copy is held.</param>
    Task<IReadOnlyList<SchemaObject>> GetOrAddAsync(
        Guid connectionKey, Func<Task<IReadOnlyList<SchemaObject>>> load, bool refresh);

    void Invalidate(Guid connectionKey);
}
