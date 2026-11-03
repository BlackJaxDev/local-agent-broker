using System.Text.Json.Serialization;

namespace LocalAgentBroker;

/// <summary>Holds optional text file policy additions from broker configuration.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BrokerRepositoryOptions
{
    public IReadOnlyList<string> AdditionalTextExtensions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AdditionalTextFileNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedPaths { get; init; } = Array.Empty<string>();
}
