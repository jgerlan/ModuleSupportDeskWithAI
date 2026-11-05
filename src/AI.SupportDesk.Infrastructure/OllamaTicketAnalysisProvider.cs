using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.SupportDesk.Application;
using Microsoft.Extensions.Options;

namespace AI.SupportDesk.Infrastructure;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class OllamaTicketAnalysisProvider(HttpClient http, IOptions<OllamaOptions> configuration)
    : ITicketAnalysisProvider
{
    public async Task<TicketAnalysisDto> AnalyzeAsync(string title, string description, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OllamaOptions options;
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
            || string.IsNullOrWhiteSpace(options.Model) || options.TimeoutSeconds <= 0
            || options.TimeoutSeconds > (uint.MaxValue - 1) / 1000)
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var payload = new
            {
                model = options.Model,
                stream = false,
                format = TicketAnalysisProtocol.Schema,
                options = new { temperature = 0 },
                messages = TicketAnalysisProtocol.Messages(title, description)
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/chat"))
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new TicketAnalysisException(response.StatusCode == HttpStatusCode.NotFound
                    || response.StatusCode == HttpStatusCode.ServiceUnavailable
                    || (int)response.StatusCode >= 500
                    ? AnalysisFailure.Unavailable : AnalysisFailure.InvalidResponse);

            var envelope = await response.Content.ReadFromJsonAsync<OllamaResponse>(TicketAnalysisProtocol.Json, deadline.Token);
            if (envelope is not { Done: true } || string.IsNullOrWhiteSpace(envelope.Message?.Content))
                throw new TicketAnalysisException(AnalysisFailure.InvalidResponse);
            return TicketAnalysisProtocol.Parse(envelope.Message.Content);
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

    private sealed record OllamaResponse(bool Done, OllamaMessage? Message);
    private sealed record OllamaMessage(string? Content);
}
