using NUnit.Framework;
using Shouldly;
using LocalAgentBroker;

namespace LocalAgentBroker.Tests.AgentOrchestration;

[TestFixture]
[NonParallelizable]
public class BrokerConfigurationTests
{
    private const string ConcurrencyVariable = "LOCAL_AGENT_BROKER_MAX_CONCURRENCY";
    private const string RetainedRunsVariable = "LOCAL_AGENT_BROKER_MAX_RUNS";
    private static readonly string[] s_settingSuffixes =
    [
        "CONFIG", "REPO_ROOT", "STATE_ROOT", "SESSIONS_ROOT", "API_KEY_ENV",
        "ANTHROPIC_API_KEY_ENV", "EDITOR_AUTH_ENV", "MAX_CONCURRENCY", "MAX_RUNS",
        "RETENTION_MINUTES", "TRACE", "LAUNCH_TRAY",
    ];
    private string _temporaryRoot = string.Empty;
    private readonly Dictionary<string, string?> _previousSettings = new();

    [SetUp]
    public void SetUp()
    {
        foreach (string suffix in s_settingSuffixes)
        {
            string name = $"LOCAL_AGENT_BROKER_{suffix}";
            _previousSettings[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }

        _temporaryRoot = Path.Combine(Path.GetTempPath(), $"local-broker-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryRoot);
    }

    [TearDown]
    public void TearDown()
    {
        foreach ((string name, string? value) in _previousSettings)
            Environment.SetEnvironmentVariable(name, value);
        _previousSettings.Clear();
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    [TestCase(null, 4, 32)]
    [TestCase("1", 1, 32)]
    [TestCase("8", 8, 32)]
    [TestCase("20", 20, 32)]
    [TestCase("32", 32, 32)]
    [TestCase("33", 33, 33)]
    [TestCase("100", 100, 100)]
    public void ConcurrencyBoundsKeepDefaultAndIncreaseAdmissionCapacity(
        string? concurrency, int expectedConcurrency, int expectedCapacity)
    {
        Environment.SetEnvironmentVariable(ConcurrencyVariable, concurrency);
        BrokerConfiguration configuration = Parse();
        configuration.MaximumConcurrentRuns.ShouldBe(expectedConcurrency);
        configuration.MaximumRetainedRuns.ShouldBe(expectedCapacity);
    }

    [TestCase("0")]
    [TestCase("101")]
    [TestCase("not-an-integer")]
    public void InvalidConcurrencyIsRejectedBeforeLaunch(string concurrency)
    {
        Environment.SetEnvironmentVariable(ConcurrencyVariable, concurrency);
        Should.Throw<ArgumentException>(Parse).Message.ShouldContain("between 1 and 100");
    }

    [TestCase("4", "4")]
    [TestCase("20", "24")]
    [TestCase("100", "256")]
    public void ExplicitSufficientCapacityIsPreserved(string concurrency, string capacity)
    {
        Environment.SetEnvironmentVariable(ConcurrencyVariable, concurrency);
        Environment.SetEnvironmentVariable(RetainedRunsVariable, capacity);
        Parse().MaximumRetainedRuns.ShouldBe(int.Parse(capacity));
    }

    [TestCase("100", "32", "must be at least")]
    [TestCase("4", "3", "between 4 and 256")]
    [TestCase("100", "257", "between 4 and 256")]
    public void InvalidAdmissionCapacityIsRejectedBeforeLaunch(
        string concurrency, string capacity, string diagnostic)
    {
        Environment.SetEnvironmentVariable(ConcurrencyVariable, concurrency);
        Environment.SetEnvironmentVariable(RetainedRunsVariable, capacity);
        Should.Throw<ArgumentException>(Parse).Message.ShouldContain(diagnostic);
    }

    [Test]
    public void ConfigPathsResolveAgainstTheConfigDirectoryAndWorkspace()
    {
        string configDirectory = Path.Combine(_temporaryRoot, "settings");
        string workspace = Path.Combine(_temporaryRoot, "workspace");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(workspace);
        string configPath = WriteConfig(configDirectory,
            """{"repositoryRoot":"../workspace","stateRoot":"state","sessionsRoot":"sessions","launchTray":false}""");

        BrokerConfiguration configuration = BrokerConfiguration.Parse(["--config", configPath]);

        configuration.RepositoryRoot.ShouldBe(workspace);
        configuration.StateRoot.ShouldBe(Path.Combine(workspace, "state"));
        configuration.SessionsRoot.ShouldBe(Path.Combine(workspace, "sessions"));
        configuration.LaunchTray.ShouldBeFalse();
    }

    [Test]
    public void CommandLineOverridesEnvironmentAndFileValues()
    {
        string configPath = WriteConfig(_temporaryRoot,
            """{"repositoryRoot":".","stateRoot":"from-file","maximumConcurrentRuns":2,"launchTray":true}""");
        Environment.SetEnvironmentVariable("LOCAL_AGENT_BROKER_STATE_ROOT", "from-environment");
        Environment.SetEnvironmentVariable(ConcurrencyVariable, "3");
        Environment.SetEnvironmentVariable("LOCAL_AGENT_BROKER_LAUNCH_TRAY", "false");

        BrokerConfiguration configuration = BrokerConfiguration.Parse(
            ["--config", configPath, "--state-root", "from-command-line", "--max-concurrency", "5", "--launch-tray", "true"]);

        configuration.StateRoot.ShouldBe(Path.Combine(_temporaryRoot, "from-command-line"));
        configuration.MaximumConcurrentRuns.ShouldBe(5);
        configuration.LaunchTray.ShouldBeTrue();
    }

    [Test]
    public void EnvironmentOverridesFileValues()
    {
        string configPath = WriteConfig(_temporaryRoot,
            """{"repositoryRoot":".","stateRoot":"from-file","maximumConcurrentRuns":2,"launchTray":true}""");
        Environment.SetEnvironmentVariable("LOCAL_AGENT_BROKER_STATE_ROOT", "from-environment");
        Environment.SetEnvironmentVariable(ConcurrencyVariable, "3");
        Environment.SetEnvironmentVariable("LOCAL_AGENT_BROKER_LAUNCH_TRAY", "false");

        BrokerConfiguration configuration = BrokerConfiguration.Parse(["--config", configPath]);

        configuration.StateRoot.ShouldBe(Path.Combine(_temporaryRoot, "from-environment"));
        configuration.MaximumConcurrentRuns.ShouldBe(3);
        configuration.LaunchTray.ShouldBeFalse();
    }

    [Test]
    public void UnknownConfigPropertyIsRejected()
    {
        string configPath = WriteConfig(_temporaryRoot, """{"repositoryRoot":".","unknown":true}""");

        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(["--config", configPath]))
            .Message.ShouldContain("Invalid broker config");
    }

    [Test]
    public void ConfigSessionIdentityPathIsPreserved()
    {
        string configPath = WriteConfig(_temporaryRoot,
            """{"repositoryRoot":".","sessionIdentityPath":["status","editorSession","name"]}""");

        BrokerConfiguration configuration = BrokerConfiguration.Parse(["--config", configPath]);

        configuration.SessionIdentityPath.ShouldBe(["status", "editorSession", "name"]);
    }

    [Test]
    public void SessionIdentityPathDefaultsToEmpty()
    {
        Parse().SessionIdentityPath.ShouldBeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public void ConfigRejectsEmptySessionIdentitySegment(string segment)
    {
        string configPath = WriteConfig(_temporaryRoot,
            $$"""{"repositoryRoot":".","sessionIdentityPath":["status","{{segment}}"]}""");

        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(["--config", configPath]))
            .Message.ShouldContain("sessionIdentityPath");
    }

    [TestCase("0", "between 1 and 100")]
    [TestCase("101", "between 1 and 100")]
    [TestCase("5", "must be at least")]
    public void ConfigNumericBoundsAreEnforced(string maximumConcurrentRuns, string diagnostic)
    {
        string configPath = WriteConfig(_temporaryRoot,
            $$"""{"repositoryRoot":".","maximumConcurrentRuns":{{maximumConcurrentRuns}},"maximumRetainedRuns":4}""");

        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(["--config", configPath]))
            .Message.ShouldContain(diagnostic);
    }

    [TestCase("0")]
    [TestCase("1441")]
    public void ConfigRetentionBoundsAreEnforced(string retentionMinutes)
    {
        string configPath = WriteConfig(_temporaryRoot,
            $$"""{"repositoryRoot":".","retentionMinutes":{{retentionMinutes}}}""");

        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(["--config", configPath]))
            .Message.ShouldContain("between 1 and 1440");
    }

    [TestCase("3")]
    [TestCase("257")]
    public void ConfigRetainedRunBoundsAreEnforced(string maximumRetainedRuns)
    {
        string configPath = WriteConfig(_temporaryRoot,
            $$"""{"repositoryRoot":".","maximumRetainedRuns":{{maximumRetainedRuns}}}""");

        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(["--config", configPath]))
            .Message.ShouldContain("between 4 and 256");
    }

    [TestCase("not-a-boolean")]
    [TestCase("1")]
    public void InvalidLaunchTrayOverrideIsRejected(string value)
    {
        Environment.SetEnvironmentVariable("LOCAL_AGENT_BROKER_LAUNCH_TRAY", value);
        Should.Throw<ArgumentException>(Parse).Message.ShouldContain("LAUNCH_TRAY");
    }

    [Test]
    public void DuplicateCommandLineFlagIsRejected()
    {
        Should.Throw<ArgumentException>(() => BrokerConfiguration.Parse(
            ["--repo-root", _temporaryRoot, "--repo-root", _temporaryRoot]))
            .Message.ShouldContain("more than once");
    }

    private static string WriteConfig(string directory, string json)
    {
        string path = Path.Combine(directory, "broker.config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private BrokerConfiguration Parse()
        => BrokerConfiguration.Parse(["--repo-root", _temporaryRoot]);
}
