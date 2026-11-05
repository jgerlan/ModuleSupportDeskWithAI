using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI.SupportDesk.Application;
using AI.SupportDesk.Domain;
using AI.SupportDesk.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AI.SupportDesk.IntegrationTests;

public sealed class TicketAnalysisTests
{
    private const string Valid = """
        {"suggestedCategory":" Hardware ","suggestedPriority":"High","summary":" Resumo ",
        "possibleCauses":[" Hipótese "],"recommendedActions":[" Diagnosticar "]}
        """;

    private static OllamaOptions Settings() => new()
    { BaseUrl = "http://ollama.test:11434", Model = "test-model", TimeoutSeconds = 1 };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private static OllamaTicketAnalysisProvider Provider(HttpMessageHandler handler, OllamaOptions? options = null) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, Options.Create(options ?? Settings()));

    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { done = true, message = new { content } }) };

    [Fact]
    public async Task Sends_configured_request_and_validates_normalized_analysis()
    {
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal("http://ollama.test:11434/api/chat", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("test-model", body.GetProperty("model").GetString());
            Assert.False(body.GetProperty("stream").GetBoolean());
            Assert.Equal(5, body.GetProperty("format").GetProperty("required").GetArrayLength());
            Assert.Equal(0, body.GetProperty("options").GetProperty("temperature").GetInt32());
            var messages = body.GetProperty("messages");
            var ticket = JsonSerializer.Deserialize<JsonElement>(messages[1].GetProperty("content").GetString()!);
            Assert.Equal("Title", ticket.GetProperty("title").GetString());
            Assert.Equal("Description", ticket.GetProperty("description").GetString());
            Assert.Equal(2, ticket.EnumerateObject().Count());
            return Response(Valid);
        });
        var result = await Provider(handler).AnalyzeAsync("Title", "Description", default);
        Assert.Equal("Hardware", result.SuggestedCategory);
        Assert.Equal(TicketPriority.High, result.SuggestedPriority);
        Assert.Equal("Resumo", result.Summary);
        Assert.Equal("Hipótese", Assert.Single(result.PossibleCauses));
        Assert.Equal("Diagnosticar", Assert.Single(result.RecommendedActions));
        Assert.Empty(TicketAnalysisDto.Create("Category", "Low", "Summary", [], []).PossibleCauses);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("priority")]
    [InlineData("numericPriority")]
    [InlineData("casePriority")]
    [InlineData("categoryLimit")]
    [InlineData("summaryLimit")]
    [InlineData("arrayLimit")]
    [InlineData("itemLimit")]
    [InlineData("blank")]
    [InlineData("wrongType")]
    [InlineData("malformed")]
    public async Task Rejects_invalid_model_content(string scenario)
    {
        var node = JsonNode.Parse(Valid)!.AsObject();
        switch (scenario)
        {
            case "missing": node.Remove("suggestedCategory"); break;
            case "null": node["possibleCauses"] = null; break;
            case "priority": node["suggestedPriority"] = "Urgent"; break;
            case "numericPriority": node["suggestedPriority"] = 0; break;
            case "casePriority": node["suggestedPriority"] = "high"; break;
            case "categoryLimit": node["suggestedCategory"] = new string('a', 101); break;
            case "summaryLimit": node["summary"] = new string('a', 2001); break;
            case "arrayLimit": node["possibleCauses"] = JsonSerializer.SerializeToNode(Enumerable.Repeat("cause", 9)); break;
            case "itemLimit": node["recommendedActions"] = new JsonArray(new string('a', 1001)); break;
            case "blank": node["recommendedActions"] = new JsonArray(" "); break;
            case "wrongType": node["summary"] = new JsonObject(); break;
        }
        using var handler = new Handler((_, _) => Task.FromResult(Response(
            scenario == "malformed" ? "private invalid output" : node.ToJsonString())));
        var exception = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.InvalidResponse, exception.Failure);
        Assert.DoesNotContain("private", exception.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"done\":true,\"message\":{\"content\":null}}")]
    [InlineData("{\"done\":false,\"message\":{\"content\":\"{}\"}}")]
    [InlineData("not json")]
    public async Task Rejects_malformed_or_incomplete_envelope(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json) }));
        var exception = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.InvalidResponse, exception.Failure);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    [InlineData(500)]
    public async Task Maps_missing_model_and_server_failures_to_unavailable(int status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var exception = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.Unavailable, exception.Failure);
    }

    [Fact]
    public async Task Maps_connection_failure_without_retaining_private_error()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("private server detail"));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler).AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
    }

    [Theory]
    [InlineData("url")]
    [InlineData("scheme")]
    [InlineData("model")]
    [InlineData("timeout")]
    [InlineData("timeoutOverflow")]
    public async Task Invalid_configuration_fails_only_when_analysis_is_requested(string field)
    {
        var options = Settings();
        switch (field)
        {
            case "url": options.BaseUrl = ""; break;
            case "scheme": options.BaseUrl = "file:///test"; break;
            case "model": options.Model = " "; break;
            case "timeout": options.TimeoutSeconds = 0; break;
            case "timeoutOverflow": options.TimeoutSeconds = int.MaxValue; break;
        }
        using var handler = new Handler((_, _) => throw new Exception("Should not send HTTP"));
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            Provider(handler, options).AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
    }

    [Fact]
    public async Task Distinguishes_deadline_from_client_cancellation()
    {
        using var started = new SemaphoreSlim(0);
        using var handler = new Handler(async (_, ct) =>
        {
            started.Release();
            await Task.Delay(Timeout.Infinite, ct);
            return Response(Valid);
        });
        var provider = Provider(handler);
        var timeout = await Assert.ThrowsAsync<TicketAnalysisException>(() => provider.AnalyzeAsync("T", "D", default));
        Assert.Equal(AnalysisFailure.Timeout, timeout.Failure);
        await started.WaitAsync();
        using var cancel = new CancellationTokenSource();
        var task = provider.AnalyzeAsync("T", "D", cancel.Token);
        await started.WaitAsync();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(AnalysisFailure.InvalidResponse, 502)]
    [InlineData(AnalysisFailure.Unavailable, 503)]
    [InlineData(AnalysisFailure.Timeout, 504)]
    [InlineData(AnalysisFailure.Authentication, 503)]
    public async Task Endpoint_maps_results_and_never_writes(AnalysisFailure? failure, int status)
    {
        var store = new ReadOnlyStore();
        var provider = new FakeProvider(failure);
        await using var app = new TestApp(store, provider);
        using var client = app.CreateClient();
        var original = TicketDto.From(store.Ticket);
        using var response = await client.PostAsync($"/api/tickets/{store.Ticket.Id}/analysis", null);
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("private", body.ToString());
        if (status == 200)
            Assert.Equal("High", body.GetProperty("suggestedPriority").GetString());
        else
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(status, body.GetProperty("status").GetInt32());
            if (failure == AnalysisFailure.Authentication)
                Assert.Equal("analysis_authentication_failed", body.GetProperty("code").GetString());
        }
        Assert.Equal(original, TicketDto.From(store.Ticket));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Missing_or_invalid_id_never_calls_provider()
    {
        var store = new ReadOnlyStore();
        var provider = new FakeProvider(null);
        await using var app = new TestApp(store, provider);
        using var client = app.CreateClient();
        using var missing = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/analysis", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await client.PostAsync("/api/tickets/invalid/analysis", null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Configuration_binding_errors_are_sanitized()
    {
        using var handler = new Handler((_, _) => throw new Exception("Should not send HTTP"));
        var provider = new OllamaTicketAnalysisProvider(new HttpClient(handler), new InvalidOptions());
        var error = await Assert.ThrowsAsync<TicketAnalysisException>(() =>
            provider.AnalyzeAsync("Title", "Description", default));
        Assert.Equal(AnalysisFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
    }

    private sealed class InvalidOptions : IOptions<OllamaOptions>
    {
        public OllamaOptions Value => throw new InvalidOperationException("private setting value");
    }
    private sealed class TestApp(ReadOnlyStore store, ITicketAnalysisProvider provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITicketStore>();
                services.RemoveAll<ITicketAnalysisProvider>();
                services.AddSingleton<ITicketStore>(store);
                services.AddSingleton(provider);
            });
        }
    }

    private sealed class FakeProvider(AnalysisFailure? failure) : ITicketAnalysisProvider
    {
        public int Calls { get; private set; }
        public Task<TicketAnalysisDto> AnalyzeAsync(string title, string description, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("Title", title);
            Assert.Equal("Description", description);
            if (failure is { } error) throw new TicketAnalysisException(error);
            return Task.FromResult(TicketAnalysisDto.Create("Category", "High", "Summary", [], []));
        }
    }

    private sealed class ReadOnlyStore : ITicketStore
    {
        public SupportTicket Ticket { get; } = SupportTicket.Create("Title", "Description", "Category",
            TicketStatus.Open, TicketPriority.Medium, DateTimeOffset.UtcNow);
        public Task<SupportTicket?> FindAsync(Guid id, bool forUpdate, CancellationToken ct)
        {
            Assert.False(forUpdate);
            return Task.FromResult(id == Ticket.Id ? Ticket : null);
        }
        public Task<TicketPage> ListAsync(TicketQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<TicketSummary> SummaryAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Add(SupportTicket ticket) => throw new Exception("Analysis must not write");
        public void Remove(SupportTicket ticket) => throw new Exception("Analysis must not write");
        public Task<bool> SaveAsync(CancellationToken ct) => throw new Exception("Analysis must not write");
    }
}
