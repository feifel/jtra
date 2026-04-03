using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace JtraServer.Controllers;

public static class JiraControllerExtensions
{
    public static IEndpointRouteBuilder MapJiraProxy(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jira");

        group.MapGet("/issue/{key}/summary", GetTicketSummary);
        group.MapPost("/issue/{key}/worklog", PostWorklog);

        return endpoints;
    }

    private static async Task<IResult> GetTicketSummary(
        string key,
        [FromHeader(Name = "X-Jira-Base-Url")] string jiraBaseUrl,
        [FromHeader(Name = "X-Jira-Pat")] string pat,
        HttpClient httpClient)
    {
        if (string.IsNullOrEmpty(jiraBaseUrl) || string.IsNullOrEmpty(pat))
        {
            return Results.BadRequest("JIRA base URL and PAT are required");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{jiraBaseUrl.TrimEnd('/')}/rest/api/2/issue/{key}?fields=summary");
            request.Headers.Add("Authorization", $"Bearer {pat}");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("fields", out var fields)
                    && fields.TryGetProperty("summary", out var summaryElement))
                {
                    var summary = summaryElement.GetString();
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        return Results.Json(new { summary });
                    }
                }

                return Results.NotFound("Ticket summary not found");
            }
            catch (JsonException)
            {
                return Results.Problem("Invalid JSON returned from JIRA");
            }
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> PostWorklog(
        string key,
        [FromBody] WorklogRequest worklog,
        [FromHeader(Name = "X-Jira-Base-Url")] string jiraBaseUrl,
        [FromHeader(Name = "X-Jira-Pat")] string pat,
        HttpClient httpClient)
    {
        if (string.IsNullOrEmpty(jiraBaseUrl) || string.IsNullOrEmpty(pat))
        {
            return Results.BadRequest("JIRA base URL and PAT are required");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{jiraBaseUrl.TrimEnd('/')}/rest/api/2/issue/{key}/worklog");
            request.Headers.Add("Authorization", $"Bearer {pat}");
            request.Content = JsonContent.Create(worklog);

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return Results.StatusCode((int)response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();
            return Results.Text(content, "application/json");
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}

public class WorklogRequest
{
    public int TimeSpentSeconds { get; set; }
    public string Started { get; set; } = string.Empty;
    public string? Comment { get; set; }
}
