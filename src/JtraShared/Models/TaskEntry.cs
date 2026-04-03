namespace JtraShared.Models;

public class TaskEntry
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subcategory { get; set; }
    public string? Ticket { get; set; }
    public string? Description { get; set; }
}
