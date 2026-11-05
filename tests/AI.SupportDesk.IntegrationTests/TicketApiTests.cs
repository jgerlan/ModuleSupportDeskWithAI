using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.SupportDesk.Application;
using AI.SupportDesk.Domain;
using AI.SupportDesk.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AI.SupportDesk.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER")))
            Skip = "Set SUPPORTDESK_TEST_SQLSERVER to enable real SQL Server integration tests.";
    }
}

public sealed class TicketApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false, namingPolicy: null) }
    };

    [SqlServerFact]
    public async Task Ticket_lifecycle_and_query_contract_work_against_migrated_sql_server()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER"))
        {
            InitialCatalog = $"SupportDeskTest_{Guid.NewGuid():N}"
        };

        await using var app = new TestApp(connection.ConnectionString);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        Assert.Equal(connection.InitialCatalog, db.Database.GetDbConnection().Database);
        Assert.False(db.Database.HasPendingModelChanges());
        try
        {
            await db.Database.MigrateAsync();
            using var client = app.CreateClient();
            Assert.Equal(new TicketSummary(0, 0, 0, 0, 0),
                await client.GetFromJsonAsync<TicketSummary>("/api/tickets/summary", Json));

            var original = new TicketInput(" Printer down ", "Contains %_ literally", "Hardware",
                TicketStatus.Open, TicketPriority.Critical);
            var first = await Create(client, original);
            var second = await Create(client, original with { Title = "Other printer", Description = "Paper jam" });
            await Create(client, original with { Title = "VPN", Description = "Connection timeout",
                Category = "Network", Priority = TicketPriority.Low });

            Assert.Equal("Printer down", first.Title);
            Assert.Equal(first, await client.GetFromJsonAsync<TicketDto>($"/api/tickets/{first.Id}", Json));

            const string filter = "/api/tickets?status=Open&priority=Critical&category=hardware&pageSize=1";
            var page = Assert.IsType<TicketPage>(await client.GetFromJsonAsync<TicketPage>(filter + "&sortDirection=asc", Json));
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(first.Id, Assert.Single(page.Items).Id);
            page = Assert.IsType<TicketPage>(await client.GetFromJsonAsync<TicketPage>(filter + "&page=2&sortDirection=asc", Json));
            Assert.Equal(second.Id, Assert.Single(page.Items).Id);
            page = Assert.IsType<TicketPage>(await client.GetFromJsonAsync<TicketPage>(filter, Json));
            Assert.Equal(second.Id, Assert.Single(page.Items).Id);
            page = Assert.IsType<TicketPage>(await client.GetFromJsonAsync<TicketPage>("/api/tickets?search=%25_", Json));
            Assert.Equal(first.Id, Assert.Single(page.Items).Id);
            page = Assert.IsType<TicketPage>(await client.GetFromJsonAsync<TicketPage>("/api/tickets?page=2147483647", Json));
            Assert.Empty(page.Items);

            var path = $"/api/tickets/{first.Id}";
            using var update = await client.PutAsJsonAsync(path, original with { Status = TicketStatus.Resolved }, Json);
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var resolved = Assert.IsType<TicketDto>(await update.Content.ReadFromJsonAsync<TicketDto>(Json));
            Assert.NotNull(resolved.ResolvedAt);
            Assert.Equal(first.CreatedAt, resolved.CreatedAt);
            Assert.Equal(resolved.UpdatedAt, resolved.ResolvedAt);
            Assert.Equal(new TicketSummary(3, 2, 0, 1, 2),
                await client.GetFromJsonAsync<TicketSummary>("/api/tickets/summary", Json));

            using var invalid = await client.PostAsJsonAsync("/api/tickets", original with { Title = " " }, Json);
            var problem = await AssertProblem(invalid, HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
            using var invalidFilter = await client.GetAsync("/api/tickets?status=0");
            await AssertProblem(invalidFilter, HttpStatusCode.BadRequest);
            using var numericEnum = await client.PostAsJsonAsync("/api/tickets", new { title = "Invalid", status = 0 });
            await AssertProblem(numericEnum, HttpStatusCode.BadRequest);

            using var deleted = await client.DeleteAsync(path);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using var missing = await client.GetAsync(path);
            await AssertProblem(missing, HttpStatusCode.NotFound);
            using var missingUpdate = await client.PutAsJsonAsync(path, original, Json);
            await AssertProblem(missingUpdate, HttpStatusCode.NotFound);
            using var missingDelete = await client.DeleteAsync(path);
            await AssertProblem(missingDelete, HttpStatusCode.NotFound);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Unexpected_errors_return_problem_details_without_exception_text()
    {
        await using var app = new TestApp("Server=(localdb)\\MSSQLLocalDB;Database=Unused;Integrated Security=true",
            services =>
            {
                services.RemoveAll<ITicketStore>();
                services.AddScoped<ITicketStore>(_ => throw new InvalidOperationException("private test diagnostic"));
            });
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/tickets");
        var problem = await AssertProblem(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("private test diagnostic", problem.GetRawText());
    }

    private static async Task<TicketDto> Create(HttpClient client, TicketInput input)
    {
        using var response = await client.PostAsJsonAsync("/api/tickets", input, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = Assert.IsType<TicketDto>(await response.Content.ReadFromJsonAsync<TicketDto>(Json));
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.AbsolutePath);
        return ticket;
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        return problem;
    }

    private sealed class TestApp(string connection, Action<IServiceCollection>? configure = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:SupportDesk"] = connection }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TicketDbContext>();
                services.RemoveAll<DbContextOptions<TicketDbContext>>();
                services.AddDbContext<TicketDbContext>(options => options.UseSqlServer(connection));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider, SequentialClock>();
                configure?.Invoke(services);
            });
        }
    }

    private sealed class SequentialClock : TimeProvider
    {
        private int seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref seconds));
    }
}
