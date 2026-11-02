using LocalAgentBroker.Integrations.XREngine;

namespace LocalAgentBroker;

/// <summary>
/// Process configuration sourced from command-line paths and environment variable names.
/// </summary>
internal sealed record BrokerConfiguration
{
    private string? _stateRoot;

    public required string RepositoryRoot { get; init; }

    public string StateRoot
    {
        get => _stateRoot ?? Path.Combine(RepositoryRoot, ".local-agent-broker");
        init => _stateRoot = value;
    }

    public string? SessionsRoot { get; init; }

    public bool IsXREngineIntegration { get; init; }

    public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

    public string AnthropicApiKeyEnvironmentVariable { get; init; } = "ANTHROPIC_API_KEY";

    public string? EditorAuthTokenEnvironmentVariable { get; init; }

    public int MaximumRetainedRuns { get; init; } = 32;

    public int MaximumConcurrentRuns { get; init; } = 4;

    public int RetentionMinutes { get; init; } = 120;

    public BrokerTraceMode TraceMode { get; init; }

    public static BrokerConfiguration Parse(string[] args)
    {
        string? repositoryRoot = null;
        string? stateRoot = null;
        string? sessionsRoot = null;
        string? integration = null;
        string apiKeyEnvironmentVariable = ReadSetting("API_KEY_ENV") ?? "OPENAI_API_KEY";
        string anthropicApiKeyEnvironmentVariable = ReadSetting("ANTHROPIC_API_KEY_ENV") ?? "ANTHROPIC_API_KEY";
        string? editorAuthEnvironmentVariable = ReadSetting("EDITOR_AUTH_ENV");

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.Equals(argument, "--repo-root", StringComparison.OrdinalIgnoreCase))
                repositoryRoot = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--state-root", StringComparison.OrdinalIgnoreCase))
                stateRoot = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--sessions-root", StringComparison.OrdinalIgnoreCase))
                sessionsRoot = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--integration", StringComparison.OrdinalIgnoreCase))
                integration = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--api-key-env", StringComparison.OrdinalIgnoreCase))
                apiKeyEnvironmentVariable = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--anthropic-api-key-env", StringComparison.OrdinalIgnoreCase))
                anthropicApiKeyEnvironmentVariable = ReadValue(args, ref index, argument);
            else if (string.Equals(argument, "--editor-auth-env", StringComparison.OrdinalIgnoreCase))
                editorAuthEnvironmentVariable = ReadValue(args, ref index, argument);
            else
                throw new ArgumentException($"Unknown broker argument '{argument}'.");
        }

        repositoryRoot ??= ReadSetting("REPO_ROOT");
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("--repo-root is required.");

        string fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(fullRepositoryRoot))
            throw new DirectoryNotFoundException($"Repository root '{fullRepositoryRoot}' does not exist.");

        integration ??= ReadSetting("INTEGRATION");
        bool isXREngineIntegration = string.Equals(integration, "xrengine", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(integration) && !isXREngineIntegration)
            throw new ArgumentException("--integration must be 'xrengine'.");
        stateRoot ??= ReadSetting("STATE_ROOT");
        sessionsRoot ??= ReadSetting("SESSIONS_ROOT");
        if (isXREngineIntegration)
        {
            stateRoot ??= XREngineLayout.GetStateRoot(fullRepositoryRoot);
            sessionsRoot ??= XREngineLayout.GetSessionsRoot(fullRepositoryRoot);
        }
        stateRoot ??= Path.Combine(fullRepositoryRoot, ".local-agent-broker");

        int maximumConcurrentRuns = ReadBoundedInteger(
            "MAX_CONCURRENCY", 4, 1, 100);
        int maximumRetainedRuns = ReadBoundedInteger(
            "MAX_RUNS", Math.Max(32, maximumConcurrentRuns), 4, 256);
        if (maximumRetainedRuns < maximumConcurrentRuns)
        {
            throw new ArgumentException(
                "MAX_RUNS must be at least MAX_CONCURRENCY.");
        }

        return new BrokerConfiguration
        {
            RepositoryRoot = fullRepositoryRoot,
            StateRoot = Path.GetFullPath(stateRoot),
            SessionsRoot = sessionsRoot is null ? null : Path.GetFullPath(sessionsRoot),
            IsXREngineIntegration = isXREngineIntegration,
            ApiKeyEnvironmentVariable = ValidateEnvironmentVariableName(apiKeyEnvironmentVariable),
            AnthropicApiKeyEnvironmentVariable = ValidateEnvironmentVariableName(anthropicApiKeyEnvironmentVariable),
            EditorAuthTokenEnvironmentVariable = string.IsNullOrWhiteSpace(editorAuthEnvironmentVariable)
                ? null
                : ValidateEnvironmentVariableName(editorAuthEnvironmentVariable),
            MaximumRetainedRuns = maximumRetainedRuns,
            MaximumConcurrentRuns = maximumConcurrentRuns,
            RetentionMinutes = ReadBoundedInteger("RETENTION_MINUTES", 120, 1, 1_440),
            TraceMode = ParseTraceMode(ReadSetting("TRACE")),
        };
    }

    public string ReadApiKey()
        => ReadProviderApiKey(ApiKeyEnvironmentVariable);

    public string ReadAnthropicApiKey()
        => ReadProviderApiKey(AnthropicApiKeyEnvironmentVariable);

    /// <summary>
    /// Gets the optional workspace used by an Anthropic key with multiple workspaces.
    /// </summary>
    public string ReadAnthropicWorkspaceId()
        => ReadProviderApiKey("ANTHROPIC_WORKSPACE_ID");

    private static string ReadProviderApiKey(string environmentVariable)
    {
        string? inheritedValue = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(inheritedValue) || !OperatingSystem.IsWindows())
            return inheritedValue ?? string.Empty;

        try
        {
            return Environment.GetEnvironmentVariable(
                environmentVariable,
                EnvironmentVariableTarget.User) ?? string.Empty;
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException
            or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    public string? ReadEditorAuthToken()
        => EditorAuthTokenEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(EditorAuthTokenEnvironmentVariable);

    private static string ReadValue(string[] args, ref int index, string argumentName)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{argumentName} requires a value.");
        return args[index];
    }

    private static string ValidateEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Any(static character => character is '=' or '\0'))
        {
            throw new ArgumentException("Environment variable names must be non-empty and cannot contain '='.");
        }

        return name.Trim();
    }

    private static string? ReadSetting(string suffix)
        => Environment.GetEnvironmentVariable($"LOCAL_AGENT_BROKER_{suffix}")
            ?? Environment.GetEnvironmentVariable($"XRE_LOCAL_AGENT_BROKER_{suffix}");

    private static int ReadBoundedInteger(string suffix, int fallback, int minimum, int maximum)
    {
        string? value = ReadSetting(suffix);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentException($"LOCAL_AGENT_BROKER_{suffix} must be an integer between {minimum} and {maximum}.");
        return parsed;
    }

    private static BrokerTraceMode ParseTraceMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "off" or "false" or "0" => BrokerTraceMode.Off,
            "metadata" or "true" or "1" => BrokerTraceMode.Metadata,
            _ => throw new ArgumentException(
                "LOCAL_AGENT_BROKER_TRACE must be 'off' or 'metadata'."),
        };
}
