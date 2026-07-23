namespace DotNetDBTasks.Application.Common.Models;

/// <summary>
/// One query parameter handed to the Word/PDF exporter so templates can print the value
/// the query ran with. <see cref="Name"/> matches the SQL <c>@name</c> and is exposed as a
/// <c>{{@Name}}</c> placeholder; <see cref="DisplayName"/> is the admin-facing label used by
/// the <c>{{PARAMS}}</c> summary placeholder. <see cref="Value"/> is the bound value (a
/// sequence for multi-value parameters).
/// </summary>
public record ExportParameter(string Name, string DisplayName, object? Value);
