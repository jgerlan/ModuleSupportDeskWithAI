using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.SupportDesk.Api;
using AI.SupportDesk.Application;
using AI.SupportDesk.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AI.SupportDesk.IntegrationTests;

public sealed class LiteLlmAnalysisTests
{
    private const string Analysis = """
        {"suggestedCategory":" Category ","suggestedPriority":"High","summary":" Summary ",
        "possibleCauses":[],"recommendedActions":[" Inspect "]}
        """;
    private static LiteLlmOptions Settings(string? key = null) => new()
    { BaseUrl = "http://proxy.test:4000/v1", Model = "supportdesk-analysis", TimeoutSeconds = 1, ApiKey = key };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private static HttpResponseMessage Response(string content = Analysis, string finishReason = "stop") =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new {
                message = new { role = "assistant", content }, finish_reason = finishReason } } })
        };

    private static LiteLlmTicketAnalysisProvider Provider(HttpMessageHandler handler, LiteLlmOptions? settings = null) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, Options.Create(settings ?? Settings()));

    [Theory]
    [InlineData(null)]
    [InlineData("test-key")]
    public async Task Sends_schema_and_optional_authentication_and_validates_result(string? key)
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal("http://proxy.test:4000/v1/chat/completions", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(key, request.Headers.Authorization?.Parameter);
            Assert.Equal(key is null ? null : "Bearer", request.Headers.Authorization?.Scheme);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("supportdesk-analysis", body.GetProperty("model").GetString());
            Assert.False(body.GetProperty("stream").GetBoolean());
            Assert.Equal(0, body.GetProperty("temperature").GetInt32());
            var format = body.GetProperty("response_format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            var schema = format.GetProperty("json_schema");
            Assert.True(schema.GetProperty("strict").GetBoolean());
            Assert.Equal(5, schema.GetProperty("schema").GetProperty("required").GetArrayLength());
            var data = JsonSerializer.Deserialize<JsonElement>(
                body.GetProperty("messages")[1].GetProperty("content").GetString()!);
            Assert.Equal("Title", data.GetProperty("title").GetString());
            Assert.Equal("Description", data.GetProperty("description").GetString());
            Assert.Equal(2, data.EnumerateObject().Count());
            return Response();
        });
        var result = await Provider(handler, Settings(key)).AnalyzeAsync("Title", "Description", default);
        Assert.Equal("Category", result.SuggestedCategory);
        Assert.Equal("Summary", result.Summary);
        Assert.Equal("Inspect", Assert.Single(result.RecommendedActions));
    }

    [Theory]
    [InlineData("http://proxy.test/v1/")]
    [InlineData("http://proxy.test/custom/v1")]
    public async Task Preserves_configured_base_path(string url)
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(url.TrimEnd('/') + "/chat/completions", request.RequestUri!.ToString());
            return Task.FromResult(Response());
        });
        var settings = Settings();
        settings.BaseUrl = url;
        await Provider(handler, settings).AnalyzeAsync("T", "D", default);
    }

    [Theory]
    [InlineData(401, AnalysisFailure.Authentication)]
    [InlineData(403, AnalysisFailure.Authentication)]
    [InlineData(404, AnalysisFailure.Unavailable)]
    [InlineData(429, AnalysisFailure.Unavailable)]
    [InlineData(500, AnalysisFailure.Unavailable)]
    [InlineData(503, AnalysisFailure.Unavailable)]
    [InlineData(408, AnalysisFailure.Timeout)]
    [InlineData(504, AnalysisFailure.Timeout)]
    [InlineData(400, AnalysisFailure.InvalidResponse)]
    public async Task Maps_proxy_errors_without_leaking_body_or_retrying(int status, AnalysisFailure expected)
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("private proxy details") });
        });
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("T", "D", default));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}")]
    [InlineData("private malformed response")]
    public async Task Rejects_malformed_or_incomplete_envelopes(string content)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(content) }));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.InvalidResponse, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"suggestedCategory\":\"Category\",\"suggestedPriority\":\"Urgent\",\"summary\":\"Summary\",\"possibleCauses\":[],\"recommendedActions\":[]}")]
    [InlineData("not JSON")]
    public async Task Uses_shared_content_validation(string content)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(content)));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Connection_failure_is_sanitized()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("private key detail"));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
    }

    [Fact]
    public async Task Distinguishes_timeout_from_client_cancellation()
    {
        using var started = new SemaphoreSlim(0);
        using var handler = new Handler(async (_, ct) =>
        {
            started.Release();
            await Task.Delay(Timeout.Infinite, ct);
            return Response();
        });
        var provider = Provider(handler);
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() => provider.AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.Timeout, error.Failure);
        await started.WaitAsync();
        using var cancel = new CancellationTokenSource();
        var pending = provider.AnalyzeAsync("T", "D", cancel.Token);
        await started.WaitAsync();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("url")]
    [InlineData("model")]
    [InlineData("timeout")]
    [InlineData("apiKey")]
    public async Task Invalid_configuration_does_not_send_http(string field)
    {
        var settings = Settings();
        switch (field)
        {
            case "url": settings.BaseUrl = "file:///test"; break;
            case "model": settings.Model = ""; break;
            case "timeout": settings.TimeoutSeconds = 0; break;
            case "apiKey": settings.ApiKey = "private\r\nheader"; break;
        }
        using var handler = new Handler((_, _) => throw new Exception("Unexpected HTTP"));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler, settings).AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
    }

    [Theory]
    [InlineData(null, typeof(OllamaTicketAnalysisProvider))]
    [InlineData("Ollama", typeof(OllamaTicketAnalysisProvider))]
    [InlineData("LiteLLM", typeof(LiteLlmTicketAnalysisProvider))]
    public void Configuration_selects_provider_and_preserves_both_adapters(string? selection, Type expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AIAnalysis:Provider"] = selection }).Build();
        var services = new ServiceCollection().AddTicketAnalysis(configuration);
        using var container = services.BuildServiceProvider();
        using var scope = container.CreateScope();
        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<ITicketAnalysisProvider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OllamaTicketAnalysisProvider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<LiteLlmTicketAnalysisProvider>());
    }

    [Fact]
    public async Task Unknown_selection_only_fails_when_requested()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AIAnalysis:Provider"] = "unknown" }).Build();
        using var container = new ServiceCollection().AddTicketAnalysis(configuration).BuildServiceProvider();
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ITicketAnalysisProvider>();
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() => provider.AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
    }
}
