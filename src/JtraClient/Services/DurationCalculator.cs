using JtraShared.Models;

namespace JtraClient.Services;

public static class DurationCalculator
{
    public static int CalculateDurationMinutes(IEnumerable<TimeEntry> allEntries, TimeEntry entry)
    {
        var entriesForDate = allEntries
            .Where(e => e.Date == entry.Date)
            .OrderBy(e => GetStartMinutesOrMax(e.StartTime))
            .ThenBy(e => e.StartTime)
            .ThenBy(e => e.Id)
            .ToList();

        var index = entriesForDate.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
        {
            return 0;
        }

        return CalculateDurationMinutes(entriesForDate, index);
    }

    public static int CalculateDurationMinutes(IReadOnlyList<TimeEntry> orderedEntries, int index)
    {
        if (index < 0 || index >= orderedEntries.Count - 1)
        {
            return 0;
        }

        var currentEntry = orderedEntries[index];
        var nextEntry = orderedEntries[index + 1];

        if (!TryParseHmToMinutes(currentEntry.StartTime, out var currentStartMinutes) ||
            !TryParseHmToMinutes(nextEntry.StartTime, out var nextStartMinutes))
        {
            return 0;
        }

        var diffMinutes = nextStartMinutes - currentStartMinutes;
        if (diffMinutes < 0)
        {
            diffMinutes += 24 * 60;
        }

        return diffMinutes;
    }

    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return string.Empty;
        }

        return $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
    }

    public static int GetStartMinutesOrMax(string? value)
    {
        return TryParseHmToMinutes(value, out var minutes) ? minutes : int.MaxValue;
    }

    private static bool TryParseHmToMinutes(string? value, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var mins))
        {
            return false;
        }

        if (hours < 0 || hours > 23 || mins < 0 || mins > 59)
        {
            return false;
        }

        minutes = (hours * 60) + mins;
        return true;
    }
}