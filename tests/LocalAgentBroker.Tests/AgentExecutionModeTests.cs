using NUnit.Framework;
using Shouldly;
using LocalAgentBroker.Orchestration;
using LocalAgentBroker;

namespace LocalAgentBroker.Tests.AgentOrchestration;

[TestFixture]
public class AgentExecutionModeTests
{
    [Test]
    public void CliRuntimeRequestWithZeroHostBudgetsIsValid()
        => AgentRequestValidator.Validate(CliRequest()).ShouldBeEmpty();

    [Test]
    public void CliRuntimeRejectsHostEnforcedTurnRetryAndTokenCaps()
    {
        AgentRunRequest request = CliRequest() with
        {
            Budget = new AgentRunBudget { MaxTurns = 3, MaxToolCalls = 0, MaxRetries = 0, MaxOutputTokens = 0 },
        };

        AgentRequestValidator.Validate(request)
            .ShouldContain(error => error.StartsWith("CLI runtimes require max_turns"));
    }

    [Test]
    public void CliRuntimeRejectsHostedToolsAndBackgroundMode()
    {
        AgentRunRequest request = CliRequest() with
        {
            UseBackgroundMode = true,
            HostedTools = [AgentHostedTool.WebSearch],
        };

        AgentRequestValidator.Validate(request)
            .ShouldContain(error => error.StartsWith("CLI runtimes do not support"));
    }

    [Test]
    public void ProviderDefaultEffortCannotAlsoBeEnforced()
    {
        AgentRunRequest request = CliRequest() with
        {
            UseProviderDefaultReasoningEffort = true,
            EnforceReasoningEffort = true,
        };

        AgentRequestValidator.Validate(request)
            .ShouldContain("provider default effort and enforced effort cannot both be selected");
    }

    [TestCase("minimal")]
    [TestCase("ultra")]
    public void CliEffortNamesAreAccepted(string effort)
        => AgentRequestValidator.Validate(CliRequest() with { ReasoningEffort = effort }).ShouldBeEmpty();

    [Test]
    public void InProcessToolProviderAllowsMutationWithoutEditorSession()
    {
        AgentRunRequest request = CliRequest() with
        {
            ToolPolicy = new AgentToolPolicy { AllowMutation = true, AllowedTools = ["set_transform"] },
        };

        AgentRequestValidator.Validate(request).ShouldContain(error => error.Contains("cannot authorize mutation"));
        AgentRequestValidator.Validate(request with { UseInProcessToolProvider = true })
            .ShouldNotContain(error => error.Contains("cannot authorize mutation"));
    }

    [Test]
    public async Task ApiOrchestratorRejectsLocalRuntimeMode()
    {
        IAgentRunExecutor executor = new AgentOrchestrator(new ScriptedAgentModelClient());

        await Should.ThrowAsync<ArgumentException>(() => executor.RunAsync(
            "run",
            CliRequest(),
            new RecordingAgentToolProvider()));
    }

    [Test]
    public async Task BrokerRejectsLocalRuntimeModeBeforeStartingRun()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"local-broker-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            using var httpClient = new HttpClient(new QueueHttpMessageHandler());
            await using var registry = new AgentRunRegistry(
                new BrokerConfiguration { RepositoryRoot = temporaryRoot },
                httpClient);

            Should.Throw<ArgumentException>(() => registry.Start(CliRequest()))
                .Message.ShouldContain("execution_mode must be api");
            registry.List(10).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static AgentRunRequest CliRequest()
        => new()
        {
            ExecutionMode = AgentExecutionMode.CodexAppServer,
            Objective = "Answer from the signed-in local runtime.",
            RequestedModel = "gpt-6-sol",
            ReasoningEffort = "medium",
            Budget = new AgentRunBudget { MaxTurns = 0, MaxToolCalls = 0, MaxRetries = 0, MaxOutputTokens = 0 },
        };
}
