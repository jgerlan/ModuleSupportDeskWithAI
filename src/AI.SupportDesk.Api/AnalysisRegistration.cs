using AI.SupportDesk.Application;
using AI.SupportDesk.Infrastructure;

namespace AI.SupportDesk.Api;

public static class AnalysisRegistration
{
    public static IServiceCollection AddTicketAnalysis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TicketAnalysisService>();
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.Configure<LiteLlmOptions>(configuration.GetSection("LiteLLM"));
        services.AddHttpClient<OllamaTicketAnalysisProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddHttpClient<LiteLlmTicketAnalysisProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<ITicketAnalysisProvider>(provider =>
            (configuration["AIAnalysis:Provider"] ?? "Ollama") switch
            {
                "Ollama" => provider.GetRequiredService<OllamaTicketAnalysisProvider>(),
                "LiteLLM" => provider.GetRequiredService<LiteLlmTicketAnalysisProvider>(),
                _ => new UnavailableAnalysisProvider()
            });
        return services;
    }

    private sealed class UnavailableAnalysisProvider : ITicketAnalysisProvider
    {
        public Task<TicketAnalysisDto> AnalyzeAsync(string title, string description, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TicketAnalysisException(AnalysisFailure.Unavailable);
        }
    }
}
