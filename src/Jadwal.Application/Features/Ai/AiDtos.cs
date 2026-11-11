namespace Jadwal.Application.Features.Ai;

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
