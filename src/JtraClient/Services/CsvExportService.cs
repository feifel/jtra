using JtraShared.Models;
using System.Text;

namespace JtraClient.Services;

public class CsvExportService
{
    public string ExportToCsv(List<TimeEntry> entries, List<TaskEntry> taskEntries, bool oldestFirst = true)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("date,start_time,category,sub_category,ticket,description,day_accumulated_hhmm,day_accumulated_days,day_target_hhmm,day_deviation_hhmm,day_deviation_days,pending_for_jira_submission");

        IEnumerable<TimeEntry> orderedEntries = oldestFirst
            ? entries.OrderBy(e => e.Date).ThenBy(e => e.StartTime)
            : entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.StartTime);

        foreach (var entry in orderedEntries)
        {
            var (category, subcategory) = ResolveLabels(entry, taskEntries);
            sb.AppendLine($"{entry.Date},{entry.StartTime},{EscapeCsv(category)},{EscapeCsv(subcategory)},{EscapeCsv(entry.Ticket)},{EscapeCsv(entry.Description)},{entry.DayAccumulatedHhmm},{entry.DayAccumulatedDays},{entry.DayTargetHhmm},{entry.DayDeviationHhmm},{entry.DayDeviationDays},{entry.PendingForJiraSubmission}");
        }

        return sb.ToString();
    }

    public List<TimeEntry> ImportFromCsv(string csvContent)
    {
        var entries = new List<TimeEntry>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            return entries;
        }

        var header = lines[0].Trim();
        if (!header.Equals("date,start_time,category,sub_category,ticket,description,day_accumulated_hhmm,day_accumulated_days,day_target_hhmm,day_deviation_hhmm,day_deviation_days,pending_for_jira_submission", StringComparison.OrdinalIgnoreCase))
        {
            return entries;
        }
        
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Length < 12) continue;

            var entry = new TimeEntry
            {
                Date = parts[0],
                StartTime = parts[1],
                Type = ParseTypeFromCategory(parts[2]),
                Ticket = parts[4],
                Description = parts[5],
                DayAccumulatedHhmm = parts[6],
                DayAccumulatedDays = string.IsNullOrEmpty(parts[7]) ? null : double.Parse(parts[7]),
                DayTargetHhmm = parts[8],
                DayDeviationHhmm = parts[9],
                DayDeviationDays = string.IsNullOrEmpty(parts[10]) ? null : double.Parse(parts[10]),
                PendingForJiraSubmission = bool.Parse(parts[11])
            };

            entries.Add(entry);
        }

        return entries;
    }

    private static TaskType ParseTypeFromCategory(string category)
    {
        if (Enum.TryParse<TaskType>(category, true, out var parsedType))
        {
            return parsedType;
        }

        return TaskType.Ticket;
    }

    private static (string Category, string Subcategory) ResolveLabels(TimeEntry entry, IEnumerable<TaskEntry> taskEntries)
    {
        if (entry.Type == TaskType.Break)
        {
            return ("Break", string.Empty);
        }

        var task = taskEntries.FirstOrDefault(t =>
            string.Equals(t.Ticket ?? string.Empty, entry.Ticket ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Description ?? string.Empty, entry.Description ?? string.Empty, StringComparison.Ordinal));

        return (task?.Category ?? entry.Type.ToString(), task?.Subcategory ?? string.Empty);
    }

    public string ExportTasksToCsv(List<TaskEntry> entries)
    {
        var sb = new StringBuilder();

        sb.AppendLine("category,subcategory,ticket,description");

        foreach (var entry in entries.OrderBy(e => e.Category).ThenBy(e => e.Subcategory))
        {
            sb.AppendLine($"{EscapeCsv(entry.Category)},{EscapeCsv(entry.Subcategory)},{EscapeCsv(entry.Ticket)},{EscapeCsv(entry.Description)}");
        }

        return sb.ToString();
    }

    public List<TaskEntry> ImportTasksFromCsv(string csvContent)
    {
        var entries = new List<TaskEntry>();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Length < 4) continue;

            entries.Add(new TaskEntry
            {
                Category = parts[0],
                Subcategory = parts[1],
                Ticket = parts[2],
                Description = parts[3]
            });
        }

        return entries;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
