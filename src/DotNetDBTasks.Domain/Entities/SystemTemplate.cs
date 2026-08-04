namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// A system-wide uploaded asset, keyed by purpose. Holds the default Word export template
/// used when a query has no template of its own (when that row is absent too, exports fall
/// back to the built-in starter layout), and the site logo shown in the top banner.
/// </summary>
public class SystemTemplate : BaseEntity
{
    /// <summary>Key of the default Word export template row.</summary>
    public const string WordDefaultKey = "word-default";

    /// <summary>Key of the site logo shown in the top banner in place of the app name.</summary>
    public const string BrandingLogoKey = "branding-logo";

    public string Key { get; set; } = string.Empty;

    /// <summary>Original file name of the uploaded asset, shown in the admin UI. Also the
    /// source of the logo's content type, which is derived from its extension.</summary>
    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();
}
