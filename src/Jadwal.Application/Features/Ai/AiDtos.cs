using Jadwal.Application.Features.Dashboards.Dtos;
using Jadwal.Domain.Enums;

namespace Jadwal.Application.Features.Ai;

/// <summary>
/// A report the AI proposed, in the shape a save takes, so it loads into the builder as it is.
/// Nothing has been saved.
/// </summary>
public class AiReportDraftDto
{
    public Features.Reports.Dtos.ReportInput Draft { get; set; } = new();

    /// <summary>The model's reason for each chart, in the same order as <c>Draft.Charts</c>.</summary>
    public List<string> ChartReasons { get; set; } = new();

    /// <summary>What was left out or changed, and why.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>A connection the AI may read. The default connection has no id; the page names it.</summary>
public class AiConnectionDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DatabaseServerType ServerType { get; set; }
}

public class AiSchemaDto
{
    public List<AiSchemaObjectDto> Objects { get; set; } = new();

    /// <summary>Whether example values go to the model with the structure (System Settings).</summary>
    public bool SampleValues { get; set; }
}

public class AiSchemaObjectDto
{
    /// <summary>How SQL names it, and how the client sends it back to pick it.</summary>
    public string QualifiedName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary><c>table</c> or <c>view</c>.</summary>
    public string Kind { get; set; } = string.Empty;
}

/// <summary>SQL the AI wrote from a description. Only shown: the admin tests and saves it.</summary>
public class AiWrittenSqlDto
{
    public string Sql { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;

    /// <summary>The @parameters the SQL uses, to add to the query's parameter list.</summary>
    public List<AiSuggestedParameterDto> Parameters { get; set; } = new();
}

public class AiSuggestedParameterDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
}

/// <summary>
/// A dashboard the AI proposed, ready for the builder: the same shape a save takes, so it loads
/// into the form as it is. Nothing has been saved.
/// </summary>
public class AiDashboardDraftDto
{
    public DashboardInput Draft { get; set; } = new();

    /// <summary>The model's reason for each tile, in the same order as <c>Draft.Tiles</c>.</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>What was left out and why — a query that would not run, a tile that did not fit.</summary>
    public List<string> Notes { get; set; } = new();
}

public class AiStatusDto
{
    /// <summary>True when both the server's configuration and System Settings allow AI.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>disabled</c> (switched off — see <see cref="ConfiguredOnServer"/> for which switch),
    /// <c>ready</c>, <c>unreachable</c> or <c>modelMissing</c>.
    /// </summary>
    public string State { get; set; } = AiStates.Disabled;

    /// <summary>False when the server's configuration has AI off, whatever System Settings say.</summary>
    public bool ConfiguredOnServer { get; set; }

    public string Model { get; set; } = string.Empty;

    /// <summary>How long a request may take, so the client waits that long and no longer.</summary>
    public int TimeoutSeconds { get; set; }
}

public static class AiStates
{
    public const string Disabled = "disabled";
    public const string Ready = "ready";
    public const string Unreachable = "unreachable";
    public const string ModelMissing = "modelMissing";
}

/// <summary>What a query does, in the language of the page that asked.</summary>
public class AiSqlExplanationDto
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
    public List<string> Cautions { get; set; } = new();
}

public class AiSqlFixDto
{
    /// <summary>What was wrong and what changed — or, when unchanged, what to check instead.</summary>
    public string Explanation { get; set; } = string.Empty;

    public string FixedSql { get; set; } = string.Empty;

    /// <summary>
    /// False when the model returned the SQL as it was, which it is told to do when the error
    /// cannot be fixed from the SQL alone (a column whose real name it cannot know, say).
    /// </summary>
    public bool Changed { get; set; }
}
