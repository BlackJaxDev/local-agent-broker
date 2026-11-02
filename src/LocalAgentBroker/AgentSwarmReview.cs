using System.Text.Json.Serialization;

namespace LocalAgentBroker;

internal sealed record AgentSwarmReview
{
    [JsonRequired]
    public bool Approved { get; init; }
    [JsonRequired]
    public string Summary { get; init; } = string.Empty;
}
