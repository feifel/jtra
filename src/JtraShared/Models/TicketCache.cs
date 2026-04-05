namespace JtraShared.Models;

public class TicketCache : IEquatable<TicketCache>
{
    public string TicketKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int UseCount { get; set; } = 1;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;

    public bool Equals(TicketCache? other) => other is not null && string.Equals(TicketKey, other.TicketKey, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => Equals(obj as TicketCache);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(TicketKey);
}