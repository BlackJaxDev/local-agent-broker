namespace LocalAgentBroker.Integrations.XREngine;

/// <summary>Resolves the storage paths used by the XRENGINE integration.</summary>
public static class XREngineLayout
{
    public static string GetStateRoot(string repositoryRoot)
        => Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "Build",
            "_AgentValidation",
            "00000000-000000-shared",
            "local-agent-broker-ui");

    public static string GetSessionsRoot(string repositoryRoot)
        => Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "Build",
            "_AgentValidation",
            "00000000-000000-shared",
            "mcp-sessions");
}
