using System.Text.Json.Serialization;

namespace LocalAgentBroker;

internal sealed record AgentSwarmPlan
{
    [JsonRequired]
    public List<AgentSwarmPlanChild> Children { get; init; } = [];
}
