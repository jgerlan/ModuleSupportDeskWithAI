using System.Text.Json.Serialization;
using AI.SupportDesk.Api;
using AI.SupportDesk.Application;
using AI.SupportDesk.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddLocalProxyKey(builder.Environment.EnvironmentName, args);
builder.Services.AddControllers(options =>
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TicketService>();
builder.Services.AddTicketAnalysis(builder.Configuration);
builder.Services.AddTicketPersistence(builder.Configuration.GetConnectionString("SupportDesk"));

var origin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:4200";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origin).AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    var analysisProvider = app.Configuration["AIAnalysis:Provider"] ?? "Ollama";
    var apiKeyConfigured = !string.IsNullOrWhiteSpace(app.Configuration["LiteLLM:ApiKey"]);
    app.Logger.LogInformation(
        "AI analysis configuration: Provider={Provider}, Environment={Environment}, LiteLLMApiKeyConfigured={Configured}",
        analysisProvider, app.Environment.EnvironmentName, apiKeyConfigured);
    if (analysisProvider == "LiteLLM" && !apiKeyConfigured)
        app.Logger.LogWarning(
            "LiteLLM selected without an API key. Run scripts/Configure-LiteLlm.ps1 and restart the API with the local launch profile.");
}
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors();
}
app.MapControllers();
app.Run();

public partial class Program { }
