namespace JtraShared.Models;

public class TicketCache
{
    public string TicketKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int UseCount { get; set; } = 1;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
}
