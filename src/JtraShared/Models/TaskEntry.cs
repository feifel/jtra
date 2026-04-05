namespace JtraShared.Models;

public class TaskEntry : IEquatable<TaskEntry>
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subcategory { get; set; }
    public string? Ticket { get; set; }
    public string? Description { get; set; }

    public bool Equals(TaskEntry? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as TaskEntry);
    public override int GetHashCode() => Id.GetHashCode();
}