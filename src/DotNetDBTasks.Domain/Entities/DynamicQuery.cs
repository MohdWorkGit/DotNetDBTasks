namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Represents a reusable, parameterized SQL query that can be assigned to roles
/// and executed by authorized users through dynamically generated forms.
/// </summary>
public class DynamicQuery : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The parameterized SQL query text. Must use @parameter syntax only.
    /// Raw string concatenation is strictly prohibited.
    /// </summary>
    public string SqlQuery { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Maximum execution time in seconds to prevent long-running queries.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, this query is executed asynchronously as a background job that the
    /// client polls for results — appropriate for slow queries that would otherwise be
    /// killed by proxy/edge timeouts. When false, the query runs synchronously and the
    /// result is returned in the same request (faster for normal, quick queries).
    /// </summary>
    public bool IsLongRunning { get; set; }

    /// <summary>
    /// When true, users running this query as a write (INSERT/UPDATE/DELETE) may opt out of the
    /// preview/confirm step and commit in one pass. When false, the option is not offered and
    /// every run goes through the preview. Defaults to true so existing queries keep the choice.
    /// </summary>
    public bool AllowRunWithoutConfirmation { get; set; } = true;

    /// <summary>
    /// When true, an UPDATE/DELETE run captures the pre-change state of every affected row into
    /// the execution log so it can be reviewed later. Turn off for statements that touch large
    /// numbers of rows: the snapshot costs an extra SELECT per run and stores a copy of every
    /// affected row. Defaults to true so existing queries keep their audit trail.
    /// </summary>
    public bool SaveOldValues { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// The database user whose credentials are used when executing this query.
    /// Null means use the system default connection string.
    /// </summary>
    public Guid? DatabaseUserId { get; set; }
    public DatabaseUser? DatabaseUser { get; set; }

    /// <summary>
    /// Optional group/folder this query belongs to. Null means the query is "ungrouped".
    /// </summary>
    public Guid? QueryGroupId { get; set; }
    public QueryGroup? QueryGroup { get; set; }

    /// <summary>
    /// Optional Word (.docx) template used by the Word export of this query's results.
    /// The template may contain {{RESULTS}} (replaced by the result table) and
    /// {{QUERY_NAME}}/{{GENERATED_AT}}/{{ROW_COUNT}} text placeholders. Null means the
    /// built-in default document layout is used.
    /// </summary>
    public byte[]? WordTemplate { get; set; }

    /// <summary>Original file name of the uploaded template, shown in the admin UI.</summary>
    public string? WordTemplateFileName { get; set; }

    public ICollection<QueryParameter> Parameters { get; set; } = new List<QueryParameter>();
    public ICollection<DynamicQueryRole> DynamicQueryRoles { get; set; } = new List<DynamicQueryRole>();
    public ICollection<DynamicQueryDepartment> DynamicQueryDepartments { get; set; } = new List<DynamicQueryDepartment>();
    public ICollection<DynamicQueryUser> DynamicQueryUsers { get; set; } = new List<DynamicQueryUser>();
    public ICollection<QueryExecutionLog> ExecutionLogs { get; set; } = new List<QueryExecutionLog>();
}
