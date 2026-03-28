namespace JtraShared.Models;

public class TicketCache
{
    public string TicketKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
