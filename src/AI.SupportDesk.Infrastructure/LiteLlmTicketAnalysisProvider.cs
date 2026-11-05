using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI.SupportDesk.Application;
using Microsoft.Extensions.Options;

namespace AI.SupportDesk.Infrastructure;

public sealed class LiteLlmOptions
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public string? ApiKey { get; set; }
}

public sealed class LiteLlmTicketAnalysisProvider(HttpClient http, IOptions<LiteLlmOptions> configuration)
    : ITicketAnalysisProvider
{
    public async Task<TicketAnalysisDto> AnalyzeAsync(string title, string description, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LiteLlmOptions options;
        try
        {
            options = configuration.Value;
        }
        catch (InvalidOperationException)
        {
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)
            || string.IsNullOrWhiteSpace(options.Model) || options.TimeoutSeconds <= 0
            || options.TimeoutSeconds > (uint.MaxValue - 1) / 1000
            || options.ApiKey?.Any(char.IsControl) == true)
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var payload = new
            {
                model = options.Model,
                stream = false,
                temperature = 0,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new { name = "ticket_analysis", strict = true, schema = TicketAnalysisProtocol.Schema }
                },
                messages = TicketAnalysisProtocol.Messages(title, description)
            };
            var endpoint = new Uri(new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/"), "chat/completions");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new TicketAnalysisException(response.StatusCode switch
                {
                    HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AnalysisFailure.Timeout,
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AnalysisFailure.Authentication,
                    HttpStatusCode.NotFound
                        or HttpStatusCode.TooManyRequests => AnalysisFailure.Unavailable,
                    _ => (int)response.StatusCode >= 500 ? AnalysisFailure.Unavailable : AnalysisFailure.InvalidResponse
                });

            var result = await response.Content.ReadFromJsonAsync<CompletionResponse>(TicketAnalysisProtocol.Json, deadline.Token);
            if (result?.Choices is not { Length: 1 } choices || choices[0]?.FinishReason != "stop")
                throw new TicketAnalysisException(AnalysisFailure.InvalidResponse);
            return TicketAnalysisProtocol.Parse(choices[0]!.Message?.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TicketAnalysisException(AnalysisFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);
        }
        catch (IOException)
        {
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);
        }
        catch (JsonException)
        {
            throw new TicketAnalysisException(AnalysisFailure.InvalidResponse);
        }
    }

    private sealed record CompletionResponse(CompletionChoice?[]? Choices);
    private sealed record CompletionChoice(CompletionMessage? Message,
        [property: System.Text.Json.Serialization.JsonPropertyName("finish_reason")] string? FinishReason);
    private sealed record CompletionMessage(string? Content);
}
