namespace JtraShared.Models;

public record ConnectionState
{
    public string? LastTaskType { get; init; }
    public string? LastTicket { get; init; }
    public string? LastDescription { get; init; }
    public string? LastStartTime { get; init; }
}