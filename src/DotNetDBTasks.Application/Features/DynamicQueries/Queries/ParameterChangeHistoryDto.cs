namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

public class ParameterChangeHistoryDto
{
    public Guid Id { get; set; }
    public Guid DynamicQueryId { get; set; }
    public string QueryName { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public Guid? ChangedByUserId { get; set; }
}
