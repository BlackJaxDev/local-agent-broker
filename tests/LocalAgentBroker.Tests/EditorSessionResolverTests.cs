using LocalAgentBroker;
using NUnit.Framework;
using Shouldly;

namespace LocalAgentBroker.Tests.AgentOrchestration;

[TestFixture]
public class EditorSessionResolverTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"local-broker-sessions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ExplicitExternalSessionRootResolvesManifest()
    {
        string workspace = Path.Combine(_root, "workspace");
        string sessionsRoot = Path.Combine(_root, "sessions");
        string sessionDirectory = Path.Combine(sessionsRoot, "run-1");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(sessionDirectory);
        string manifestPath = Path.Combine(sessionDirectory, "session.json");
        File.WriteAllText(manifestPath,
            """{"name":"editor-test","endpoint":"http://127.0.0.1:49152/mcp"}""");

        ResolvedEditorSession session = new EditorSessionResolver(sessionsRoot).Resolve("editor-test");

        session.ManifestPath.ShouldBe(manifestPath);
        session.Endpoint.Host.ShouldBe("127.0.0.1");
    }

    [Test]
    public void MissingSessionRootDisablesResolution()
    {
        Should.Throw<InvalidOperationException>(() => new EditorSessionResolver(null).Resolve("editor-test"));
    }
}
