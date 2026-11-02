using System.Text.Json.Serialization;
using LocalAgentBroker.Orchestration;

namespace LocalAgentBroker;

internal sealed record AgentSwarmPlanChild
{
    [JsonRequired]
    public string Objective { get; init; } = string.Empty;
    [JsonRequired]
    public List<string> Paths { get; init; } = [];
    [JsonRequired]
    public AgentSwarmRole Role { get; init; }
}
