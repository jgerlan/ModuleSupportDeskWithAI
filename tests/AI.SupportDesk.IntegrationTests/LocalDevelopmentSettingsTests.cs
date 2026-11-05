using AI.SupportDesk.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.SupportDesk.IntegrationTests;

public sealed class LocalDevelopmentSettingsTests
{
    [Theory]
    [InlineData("Development", null, "local-test-key")]
    [InlineData("Development", "", "local-test-key")]
    [InlineData("Development", "secret-test-key", "secret-test-key")]
    [InlineData("Production", null, null)]
    public void Local_key_is_only_a_development_fallback(string environment, string? existing, string? expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "supportdesk-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.Local.json"),
                """{"LiteLLM":{"ApiKey":"local-test-key"}}""");
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(directory);
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["LiteLLM:ApiKey"] = existing });
            configuration.AddLocalProxyKey(environment, []);
            Assert.Equal(expected, configuration["LiteLLM:ApiKey"]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Command_line_overrides_local_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), "supportdesk-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.Local.json"),
                """{"LiteLLM":{"ApiKey":"local-test-key"}}""");
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(directory);
            configuration.AddLocalProxyKey("Development", ["--LiteLLM:ApiKey=command-line-test-key"]);
            Assert.Equal("command-line-test-key", configuration["LiteLLM:ApiKey"]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Missing_local_file_does_not_prevent_startup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "supportdesk-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(directory);
            configuration.AddLocalProxyKey("Development", []);
            Assert.True(string.IsNullOrEmpty(configuration["LiteLLM:ApiKey"]));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
