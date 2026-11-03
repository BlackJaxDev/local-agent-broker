namespace LocalAgentBroker;

/// <summary>Holds the settings for one broker process.</summary>
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
    public IReadOnlyList<string> SessionIdentityPath { get; init; } = Array.Empty<string>();
    public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";
    public string AnthropicApiKeyEnvironmentVariable { get; init; } = "ANTHROPIC_API_KEY";
    public string? EditorAuthTokenEnvironmentVariable { get; init; }
    public int MaximumRetainedRuns { get; init; } = 32;
    public int MaximumConcurrentRuns { get; init; } = 4;
    public int RetentionMinutes { get; init; } = 120;
    public BrokerTraceMode TraceMode { get; init; }
    public bool LaunchTray { get; init; } = true;
    public BrokerRepositoryOptions RepositoryAccess { get; init; } = new();

    public static BrokerConfiguration Parse(string[] args)
    {
        Dictionary<string, string> arguments = ParseArguments(args);
        string? configPath = Select(arguments, "--config", "CONFIG", null);
        BrokerConfigurationFile file = configPath is null
            ? new BrokerConfigurationFile()
            : BrokerConfigurationFile.Load(configPath);

        string? repositoryRoot = Select(arguments, "--repo-root", "REPO_ROOT", file.RepositoryRoot);
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("Set --repo-root, LOCAL_AGENT_BROKER_REPO_ROOT, or repositoryRoot in --config.");

        string repositoryBase = arguments.ContainsKey("--repo-root") || ReadSetting("REPO_ROOT") is not null
            ? Environment.CurrentDirectory
            : file.DirectoryPath ?? Environment.CurrentDirectory;
        string fullRepositoryRoot = Path.GetFullPath(repositoryRoot, repositoryBase);
        if (!Directory.Exists(fullRepositoryRoot))
            throw new DirectoryNotFoundException($"Repository root '{fullRepositoryRoot}' does not exist.");

        string? stateRoot = Select(arguments, "--state-root", "STATE_ROOT", file.StateRoot);
        string? sessionsRoot = Select(arguments, "--sessions-root", "SESSIONS_ROOT", file.SessionsRoot);
        int maximumConcurrentRuns = ReadBoundedInteger(arguments, "--max-concurrency", "MAX_CONCURRENCY",
            file.MaximumConcurrentRuns, 4, 1, 100);
        int maximumRetainedRuns = ReadBoundedInteger(arguments, "--max-runs", "MAX_RUNS",
            file.MaximumRetainedRuns, Math.Max(32, maximumConcurrentRuns), 4, 256);
        if (maximumRetainedRuns < maximumConcurrentRuns)
            throw new ArgumentException("MAX_RUNS must be at least MAX_CONCURRENCY.");

        int retentionMinutes = ReadBoundedInteger(arguments, "--retention-minutes", "RETENTION_MINUTES",
            file.RetentionMinutes, 120, 1, 1_440);
        string? launchTray = Select(arguments, "--launch-tray", "LAUNCH_TRAY", null);
        IReadOnlyList<string> identityPath = file.SessionIdentityPath ?? Array.Empty<string>();
        if (identityPath.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("sessionIdentityPath must contain nonempty JSON property names.");

        return new BrokerConfiguration
        {
            RepositoryRoot = fullRepositoryRoot,
            StateRoot = Path.GetFullPath(stateRoot ?? ".local-agent-broker", fullRepositoryRoot),
            SessionsRoot = sessionsRoot is null ? null : Path.GetFullPath(sessionsRoot, fullRepositoryRoot),
            SessionIdentityPath = identityPath.ToArray(),
            ApiKeyEnvironmentVariable = ValidateEnvironmentVariableName(
                Select(arguments, "--api-key-env", "API_KEY_ENV", file.ApiKeyEnvironmentVariable) ?? "OPENAI_API_KEY"),
            AnthropicApiKeyEnvironmentVariable = ValidateEnvironmentVariableName(
                Select(arguments, "--anthropic-api-key-env", "ANTHROPIC_API_KEY_ENV",
                    file.AnthropicApiKeyEnvironmentVariable) ?? "ANTHROPIC_API_KEY"),
            EditorAuthTokenEnvironmentVariable = ValidateOptionalEnvironmentVariableName(
                Select(arguments, "--editor-auth-env", "EDITOR_AUTH_ENV", file.EditorAuthTokenEnvironmentVariable)),
            MaximumConcurrentRuns = maximumConcurrentRuns,
            MaximumRetainedRuns = maximumRetainedRuns,
            RetentionMinutes = retentionMinutes,
            TraceMode = ParseTraceMode(Select(arguments, "--trace", "TRACE", file.Trace)),
            LaunchTray = launchTray is null ? file.LaunchTray ?? true : ParseBoolean(launchTray, "LAUNCH_TRAY"),
            RepositoryAccess = file.RepositoryAccess ?? new BrokerRepositoryOptions(),
        };
    }

    public string ReadApiKey()
        => ReadProviderApiKey(ApiKeyEnvironmentVariable);

    public string ReadAnthropicApiKey()
        => ReadProviderApiKey(AnthropicApiKeyEnvironmentVariable);

    /// <summary>Gets the optional workspace used by an Anthropic key with multiple workspaces.</summary>
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

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        HashSet<string> supported = new(StringComparer.OrdinalIgnoreCase)
        {
            "--config", "--repo-root", "--state-root", "--sessions-root", "--api-key-env",
            "--anthropic-api-key-env", "--editor-auth-env", "--max-concurrency", "--max-runs",
            "--retention-minutes", "--trace", "--launch-tray",
        };
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!supported.Contains(argument))
                throw new ArgumentException($"Unknown broker argument '{argument}'.");

            index++;
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException($"{argument} requires a value.");
            if (!values.TryAdd(argument, args[index]))
                throw new ArgumentException($"{argument} was specified more than once.");
        }

        return values;
    }

    private static string? Select(Dictionary<string, string> arguments, string flag, string suffix, string? fileValue)
        => arguments.TryGetValue(flag, out string? value) ? value : ReadSetting(suffix) ?? fileValue;

    private static string ValidateEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(static character => character is '=' or '\0'))
            throw new ArgumentException("Environment variable names must be non-empty and cannot contain '=' or NUL.");

        return name.Trim();
    }

    private static string? ValidateOptionalEnvironmentVariableName(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : ValidateEnvironmentVariableName(name);

    private static string? ReadSetting(string suffix)
        => Environment.GetEnvironmentVariable($"LOCAL_AGENT_BROKER_{suffix}");

    private static int ReadBoundedInteger(Dictionary<string, string> arguments, string flag, string suffix,
        int? fileValue, int fallback, int minimum, int maximum)
    {
        string? value = Select(arguments, flag, suffix, fileValue?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
            throw new ArgumentException($"{flag} / LOCAL_AGENT_BROKER_{suffix} must be an integer between {minimum} and {maximum}.");
        return parsed;
    }

    private static bool ParseBoolean(string value, string setting)
    {
        if (bool.TryParse(value, out bool parsed))
            return parsed;
        throw new ArgumentException($"{setting} must be 'true' or 'false'.");
    }

    private static BrokerTraceMode ParseTraceMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "off" or "false" or "0" => BrokerTraceMode.Off,
            "metadata" or "true" or "1" => BrokerTraceMode.Metadata,
            _ => throw new ArgumentException("TRACE must be 'off' or 'metadata'."),
        };
}
