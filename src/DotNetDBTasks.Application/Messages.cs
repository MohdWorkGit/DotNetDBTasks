namespace DotNetDBTasks.Application;

/// <summary>
/// Marker type that anchors <c>IStringLocalizer&lt;Messages&gt;</c> to
/// <c>Resources/Messages.resx</c> in this assembly.
///
/// <para>
/// It must live at the Application assembly root: ASP.NET builds the resource base name from
/// the type's full name plus the configured <c>ResourcesPath</c>, so
/// <c>DotNetDBTasks.Application.Messages</c> + <c>Resources</c> resolves to
/// <c>DotNetDBTasks.Application.Resources.Messages</c>. Moving this class into a sub-namespace
/// silently breaks lookup — every key would fall back to its own name.
/// </para>
/// </summary>
public sealed class Messages
{
}
