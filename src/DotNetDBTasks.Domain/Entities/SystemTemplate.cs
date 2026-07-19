namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// A system-wide document template, keyed by purpose. Currently holds the default
/// Word export template used when a query has no template of its own; when this row
/// is absent too, exports fall back to the built-in starter layout.
/// </summary>
public class SystemTemplate : BaseEntity
{
    /// <summary>Key of the default Word export template row.</summary>
    public const string WordDefaultKey = "word-default";

    public string Key { get; set; } = string.Empty;

    /// <summary>Original file name of the uploaded template, shown in the admin UI.</summary>
    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();
}
