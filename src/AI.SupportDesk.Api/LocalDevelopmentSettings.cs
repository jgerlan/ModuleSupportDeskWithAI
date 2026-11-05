namespace AI.SupportDesk.Api;

public static class LocalDevelopmentSettings
{
    public static void AddLocalProxyKey(this ConfigurationManager configuration, string environmentName, string[] args)
    {
        if (!environmentName.Equals(Environments.Development, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(configuration["LiteLLM:ApiKey"]))
            return;

        configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        // Preserve explicit environment and command-line overrides after the local fallback.
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
