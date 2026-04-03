using System.Text.Json;
using JtraShared.Models;

namespace JtraClient.Services;

public class JiraTicketService
{
    private readonly HttpClient _httpClient;

    public JiraTicketService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetTicketSummaryAsync(AppSettings settings, string ticketKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.JiraBaseUrl) || string.IsNullOrWhiteSpace(settings.Pat))
        {
            throw new InvalidOperationException("Configure the JIRA Base URL and PAT in Settings before adding tickets.");
        }

        if (string.IsNullOrWhiteSpace(ticketKey))
        {
            throw new InvalidOperationException("Enter a ticket number first.");
        }

        if (!IsSafeTicketKey(ticketKey))
        {
            throw new InvalidOperationException("Ticket number contains invalid characters.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/jira/issue/{ticketKey}/summary");
        request.Headers.Add("X-Jira-Base-Url", settings.JiraBaseUrl);
        request.Headers.Add("X-Jira-Pat", settings.Pat);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Ticket '{ticketKey}' was not found in JIRA.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorBody)
                    ? $"JIRA lookup failed for ticket '{ticketKey}' with status {(int)response.StatusCode}."
                    : $"JIRA lookup failed for ticket '{ticketKey}': {errorBody}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(jsonContent);
        
        if (document.RootElement.TryGetProperty("summary", out var summaryElement))
        {
            var summary = summaryElement.GetString();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }
        }

        throw new InvalidOperationException($"Ticket '{ticketKey}' does not have a summary in JIRA.");
    }

    private static bool IsSafeTicketKey(string ticketKey)
    {
        for (int i = 0; i < ticketKey.Length; i++)
        {
            var c = ticketKey[i];
            var isUpperLetter = c >= 'A' && c <= 'Z';
            var isDigit = c >= '0' && c <= '9';
            if (!isUpperLetter && !isDigit && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}