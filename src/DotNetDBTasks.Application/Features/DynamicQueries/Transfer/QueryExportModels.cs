using DotNetDBTasks.Domain.Enums;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Transfer;

/// <summary>
/// The portable form of one or more queries — what an export writes and an import reads.
///
/// Everything that points at another record travels as a **name**, never an id: a backup
/// restored onto a different system would otherwise carry GUIDs that resolve to nothing (or,
/// worse, to something unrelated). Import re-resolves each name against the target system and
/// reports whatever it cannot find.
/// </summary>
public class QueryExportFile
{
    /// <summary>Bumped when the shape changes, so import can reject files it cannot read.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public const int CurrentFormatVersion = 1;

    public DateTime ExportedAt { get; set; }
    public string? ExportedBy { get; set; }
    public List<ExportedQuery> Queries { get; set; } = new();
}

public class ExportedQuery
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public bool IsLongRunning { get; set; }
    public bool AllowRunWithoutConfirmation { get; set; } = true;
    public bool SaveOldValues { get; set; } = true;

    /// <summary>
    /// Name of the database connection this query runs against, for re-matching on the target.
    /// Credentials are never exported — see the remarks on <see cref="ExportQueriesQuery"/>.
    /// </summary>
    public string? DatabaseUserName { get; set; }

    /// <summary>Group name; import creates the group when it does not already exist.</summary>
    public string? QueryGroupName { get; set; }

    public string? WordTemplateFileName { get; set; }
    /// <summary>Base64 of the .docx, so a restored query produces identical Word exports.</summary>
    public string? WordTemplateBase64 { get; set; }

    public List<ExportedParameter> Parameters { get; set; } = new();
    public List<string> AssignedRoles { get; set; } = new();
    public List<string> AssignedDepartments { get; set; } = new();
    public List<string> AssignedUsers { get; set; } = new();

    // QueryType is deliberately absent: it is derived from SqlQuery, and re-deriving it on
    // import is both simpler and safer than trusting a value from an external file.
}

public class ExportedParameter
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public int SortOrder { get; set; }
    public bool AllowMultiple { get; set; }
    public DropdownSourceType? DropdownSourceType { get; set; }
    public string? DropdownStaticValues { get; set; }

    /// <summary>
    /// Name of the query that supplies this dropdown's options. Import resolves it after every
    /// query in the file exists, so a dropdown can point at another query from the same backup.
    /// </summary>
    public string? DropdownQueryName { get; set; }
    public string? DropdownQueryValueColumn { get; set; }
    public string? DropdownQueryLabelColumn { get; set; }
}

/// <summary>What an import actually did, surfaced to the admin rather than silently applied.</summary>
public class QueryImportResult
{
    public List<ImportedQueryResult> Queries { get; set; } = new();

    /// <summary>Names that could not be resolved on this system (roles, users, DB connections).</summary>
    public List<string> Warnings { get; set; } = new();

    public int ImportedCount => Queries.Count;
    public int RenamedCount => Queries.Count(q => q.WasRenamed);
}

public class ImportedQueryResult
{
    public string OriginalName { get; set; } = string.Empty;
    public string ImportedName { get; set; } = string.Empty;
    /// <summary>True when a query of this name already existed, so this came in as a copy.</summary>
    public bool WasRenamed { get; set; }
}
