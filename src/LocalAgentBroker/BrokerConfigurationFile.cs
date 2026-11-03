using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalAgentBroker;

/// <summary>Reads an explicitly selected JSON configuration file.</summary>
internal sealed class BrokerConfigurationFile
{
    public string? RepositoryRoot { get; init; }
    public string? StateRoot { get; init; }
    public string? SessionsRoot { get; init; }
    public IReadOnlyList<string>? SessionIdentityPath { get; init; }
    public string? ApiKeyEnvironmentVariable { get; init; }
    public string? AnthropicApiKeyEnvironmentVariable { get; init; }
    public string? EditorAuthTokenEnvironmentVariable { get; init; }
    public int? MaximumConcurrentRuns { get; init; }
    public int? MaximumRetainedRuns { get; init; }
    public int? RetentionMinutes { get; init; }
    public string? Trace { get; init; }
    public bool? LaunchTray { get; init; }
    public BrokerRepositoryOptions? RepositoryAccess { get; init; }

    [JsonIgnore]
    public string? DirectoryPath { get; private set; }

    public static BrokerConfigurationFile Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            string json = File.ReadAllText(fullPath);
            BrokerConfigurationFile? file = JsonSerializer.Deserialize<BrokerConfigurationFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
            if (file is null)
                throw new ArgumentException($"Broker config '{fullPath}' must contain a JSON object.");

            file.DirectoryPath = Path.GetDirectoryName(fullPath);
            return file;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Invalid broker config '{fullPath}': {exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new ArgumentException($"Cannot read broker config '{fullPath}': {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ArgumentException($"Cannot read broker config '{fullPath}': {exception.Message}", exception);
        }
    }
}
