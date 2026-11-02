namespace LocalAgentBroker.Shared;

/// <summary>
/// Resolves the storage used by the broker tray companion.
/// </summary>
public sealed class BrokerUiPaths
{
    public BrokerUiPaths(string repositoryRoot)
        : this(repositoryRoot, Path.Combine(repositoryRoot, ".local-agent-broker"))
    {
    }

    public BrokerUiPaths(string repositoryRoot, string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        RootDirectory = Path.GetFullPath(stateRoot);
        RunsDirectory = Path.Combine(RootDirectory, "runs");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
    }

    public string RepositoryRoot { get; }

    public string RootDirectory { get; }

    public string RunsDirectory { get; }

    public string SettingsPath { get; }
}
