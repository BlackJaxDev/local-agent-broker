using System.Text.Json;
using LocalAgentBroker.Orchestration;

namespace LocalAgentBroker;

/// <summary>
/// Writes opt-in, metadata-only traces below the configured state root.
/// </summary>
internal sealed class BrokerTraceWriter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _traceRoot;
    private readonly BrokerTraceMode _mode;

    public BrokerTraceWriter(BrokerConfiguration configuration)
    {
        _traceRoot = Path.Combine(configuration.StateRoot, "traces");
        _mode = configuration.TraceMode;
    }

    public void Write(BrokerRunRecord record, AgentRunResult result)
    {
        if (_mode == BrokerTraceMode.Off)
            return;

        try
        {
            Directory.CreateDirectory(_traceRoot);
            if (!CanCreateTraceRoot())
                return;

            string timestamp = record.CreatedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss");
            string runRoot = Path.Combine(
                _traceRoot,
                $"{timestamp}-local-agent-broker-{record.RunId[..8]}");
            Directory.CreateDirectory(Path.Combine(runRoot, "reports"));

            var metadata = new
            {
                result.RunId,
                result.Status,
                result.RequestedModel,
                result.ActualModel,
                record.Request.EditorSession,
                record.Request.UseBackgroundMode,
                objectiveLength = record.Request.Objective.Length,
                successCriteriaCount = record.Request.SuccessCriteria.Count,
                constraintCount = record.Request.Constraints.Count,
                record.Request.Budget,
                toolPolicy = new
                {
                    record.Request.ToolPolicy.AllowMutation,
                    record.Request.ToolPolicy.AllowDestructive,
                    record.Request.ToolPolicy.RequireMutationEvidence,
                    allowedToolCount = record.Request.ToolPolicy.AllowedTools.Count,
                    deniedToolCount = record.Request.ToolPolicy.DeniedTools.Count,
                },
                result.Usage,
                result.ToolCallCount,
                result.TurnCount,
                result.RetryCount,
                providerAttempts = result.ProviderAttempts.Select(static attempt => new
                {
                    attempt.TurnNumber,
                    attempt.AttemptNumber,
                    attempt.UsedBackgroundMode,
                    attempt.Outcome,
                    attempt.ResponseId,
                    attempt.ActualModel,
                    attempt.ProviderEventCount,
                    attempt.MalformedEventCount,
                    attempt.LastProviderEventType,
                    attempt.LastSequenceNumber,
                    attempt.TerminalStatus,
                    attempt.IncompleteReason,
                    attempt.ElapsedMilliseconds,
                    attempt.FailureCategory,
                    attempt.ProviderStatus,
                    attempt.Retryable,
                    attempt.Retried,
                    attempt.ProviderCancellationAccepted,
                }),
                result.ElapsedMilliseconds,
                failure = result.Failure is null
                    ? null
                    : new
                    {
                        result.Failure.Category,
                        result.Failure.Summary,
                        result.Failure.Retryable,
                        result.Failure.ProviderStatus,
                    },
            };
            string path = Path.Combine(runRoot, "reports", "run-metadata.json");
            File.WriteAllText(path, JsonSerializer.Serialize(metadata, s_jsonOptions));
        }
        catch
        {
            // Optional diagnostics must never change a run's terminal result.
        }
    }

    private bool CanCreateTraceRoot()
    {
        int immediateDirectoryCount = Directory
            .EnumerateDirectories(_traceRoot, "*", SearchOption.TopDirectoryOnly)
            .Take(10)
            .Count();
        return immediateDirectoryCount < 10;
    }
}
