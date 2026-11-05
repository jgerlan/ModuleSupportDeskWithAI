using AI.SupportDesk.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AI.SupportDesk.Api;

internal sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
            return true;

        ProblemDetails problem = exception switch
        {
            TicketValidationException validation => new HttpValidationProblemDetails(validation.Errors)
            {
                Status = 400,
                Title = "One or more validation errors occurred."
            },
            TicketAnalysisException analysis => AnalysisProblem(analysis.Failure),
            _ => new ProblemDetails { Status = 500, Title = "An unexpected server error occurred." }
        };

        context.Response.StatusCode = problem.Status!.Value;
        if (!await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
            await context.Response.WriteAsJsonAsync<object>(problem, options: null,
                contentType: "application/problem+json", cancellationToken);
        return true;
    }

    private static ProblemDetails AnalysisProblem(AnalysisFailure failure) => failure switch
    {
        AnalysisFailure.Authentication => new ProblemDetails
        {
            Status = 503,
            Title = "A API não conseguiu autenticar no LiteLLM. Verifique LiteLLM:ApiKey e reinicie o backend.",
            Extensions = { ["code"] = "analysis_authentication_failed" }
        },
        AnalysisFailure.InvalidResponse => new ProblemDetails
        {
            Status = 502,
            Title = "O serviço de IA retornou uma análise inválida. Tente novamente."
        },
        AnalysisFailure.Timeout => new ProblemDetails
        {
            Status = 504,
            Title = "A análise excedeu o tempo limite. Tente novamente."
        },
        _ => new ProblemDetails
        {
            Status = 503,
            Title = "O serviço de IA está indisponível. Verifique o provedor e o modelo configurados."
        }
    };
}
