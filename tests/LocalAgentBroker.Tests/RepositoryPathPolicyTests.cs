using LocalAgentBroker;
using NUnit.Framework;
using Shouldly;

namespace LocalAgentBroker.Tests.AgentOrchestration;

[TestFixture]
public class RepositoryPathPolicyTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"local-broker-path-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void AdditionalTextTypesAllowOnlyNamedTypes()
    {
        File.WriteAllText(Path.Combine(_root, "source.custom"), "source");
        File.WriteAllText(Path.Combine(_root, "BUILDINFO"), "source");
        File.WriteAllText(Path.Combine(_root, "source.binary"), "source");
        RepositoryPathPolicy policy = new(_root, options: new BrokerRepositoryOptions
        {
            AdditionalTextExtensions = [".custom"],
            AdditionalTextFileNames = ["BUILDINFO"],
        });

        policy.ResolveTextFile("source.custom").ShouldBe(Path.Combine(_root, "source.custom"));
        policy.ResolveTextFile("BUILDINFO").ShouldBe(Path.Combine(_root, "BUILDINFO"));
        Should.Throw<ArgumentException>(() => policy.ResolveTextFile("source.binary"));
    }

    [Test]
    public void AdditionalTextTypesCannotOverrideSecretRules()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "secret");
        File.WriteAllText(Path.Combine(_root, "private.key"), "secret");
        RepositoryPathPolicy policy = new(_root, options: new BrokerRepositoryOptions
        {
            AdditionalTextExtensions = [".key"],
            AdditionalTextFileNames = [".env"],
        });

        Should.Throw<ArgumentException>(() => policy.ResolveTextFile(".env"));
        Should.Throw<ArgumentException>(() => policy.ResolveTextFile("private.key"));
        Should.Throw<ArgumentException>(() => policy.ResolvePotentialTextFile("credentials.json"));
    }

    [Test]
    public void ConfiguredPathsAndStorageRootsStayExcluded()
    {
        string excluded = CreateTextFile("private", "data.cs");
        string state = CreateTextFile("custom-state", "data.cs");
        string sessions = CreateTextFile("custom-sessions", "data.cs");
        string allowed = CreateTextFile("source", "data.cs");
        RepositoryPathPolicy policy = new(_root,
            stateRoot: Path.Combine(_root, "custom-state"),
            options: new BrokerRepositoryOptions { ExcludedPaths = ["private"] },
            sessionsRoot: Path.Combine(_root, "custom-sessions"));

        policy.ResolveTextFile("source/data.cs").ShouldBe(allowed);
        Should.Throw<ArgumentException>(() => policy.ResolveTextFile("private/data.cs"));
        Should.Throw<ArgumentException>(() => policy.ResolveTextFile("custom-state/data.cs"));
        Should.Throw<ArgumentException>(() => policy.ResolveTextFile("custom-sessions/data.cs"));
        policy.IsTextFileCandidate(excluded).ShouldBeFalse();
        policy.IsTextFileCandidate(state).ShouldBeFalse();
        policy.IsTextFileCandidate(sessions).ShouldBeFalse();
    }

    [Test]
    public void ConfiguredExclusionsDoNotMatchSiblingNames()
    {
        string sibling = CreateTextFile("private-copy", "data.cs");
        RepositoryPathPolicy policy = new(_root,
            options: new BrokerRepositoryOptions { ExcludedPaths = ["private"] });

        policy.ResolveTextFile("private-copy/data.cs").ShouldBe(sibling);
    }

    [TestCase("../outside.cs")]
    [TestCase("source/../data.cs")]
    [TestCase("Build/data.cs")]
    public void TraversalAndGeneratedOutputRemainExcluded(string path)
    {
        RepositoryPathPolicy policy = new(_root);

        Should.Throw<ArgumentException>(() => policy.ResolvePotentialTextFile(path));
    }

    [TestCase("../outside")]
    [TestCase("")]
    public void InvalidConfiguredExcludedPathIsRejected(string path)
    {
        Should.Throw<ArgumentException>(() => new RepositoryPathPolicy(_root,
            options: new BrokerRepositoryOptions { ExcludedPaths = [path] }));
    }

    private string CreateTextFile(string directory, string fileName)
    {
        string fullDirectory = Path.Combine(_root, directory);
        Directory.CreateDirectory(fullDirectory);
        string path = Path.Combine(fullDirectory, fileName);
        File.WriteAllText(path, "source");
        return path;
    }
}
