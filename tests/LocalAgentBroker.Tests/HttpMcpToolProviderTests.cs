using System.Net;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Shouldly;
using LocalAgentBroker.Orchestration;

namespace LocalAgentBroker.Tests.AgentOrchestration;

[TestFixture]
public class HttpMcpToolProviderTests
{
    [Test]
    public async Task EnforcesReadOnlyDefaultAndPreservesMcpToolErrors()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(request => JsonResponse(
            """
            {"jsonrpc":"2.0","id":"1","result":{"tools":[
              {"name":"get_node","description":"read","inputSchema":{"type":"object"},"annotations":{"readOnlyHint":true,"destructiveHint":false}},
              {"name":"set_node","description":"write","inputSchema":{"type":"object"},"annotations":{"readOnlyHint":false,"destructiveHint":false}}
            ]}}
            """));
        handler.Enqueue(request => JsonResponse(
            """{"jsonrpc":"2.0","id":"2","error":{"code":-32001,"message":"editor rejected secret-value call"}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client,
            new Uri("http://localhost:5467/mcp/"),
            new AgentToolPolicy(),
            "secret-value");

        IReadOnlyList<AgentToolDefinition> tools = await provider.ListToolsAsync(CancellationToken.None);
        AgentToolResult result = await provider.ExecuteAsync(
            new AgentToolCall { CallId = "call", Name = "get_node", ArgumentsJson = "{}" },
            CancellationToken.None);

        tools.Select(static tool => tool.Name).ShouldBe(["get_node"]);
        result.IsError.ShouldBeTrue();
        result.Content.ShouldContain("editor rejected [REDACTED] call");
        result.Content.ShouldNotContain("secret-value");
    }

    [Test]
    public async Task PreflightVerifiesExactNamedSession()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(request => JsonResponse(
            """
            {"jsonrpc":"2.0","id":"1","result":{"ok":true,"status":{"editorSession":{"name":"other"}}}}
            """));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client,
            new Uri("http://localhost:5467/mcp/"),
            new AgentToolPolicy());

        AgentToolProviderException exception = await Should.ThrowAsync<AgentToolProviderException>(
            () => provider.PreflightAsync("expected", CancellationToken.None,
                ["status", "editorSession", "name"]));

        exception.Category.ShouldBe(AgentFailureCategory.ToolDiscovery);
    }

    [Test]
    public async Task PreflightReturnsConfiguredSessionIdentity()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """{"jsonrpc":"2.0","id":"1","result":{"status":{"editorSession":{"name":"expected"}}}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client, new Uri("http://localhost:5467/mcp/"), new AgentToolPolicy());

        string? identity = await provider.PreflightAsync("expected", CancellationToken.None,
            ["status", "editorSession", "name"]);

        identity.ShouldBe("expected");
    }

    [TestCase("""{"jsonrpc":"2.0","id":"1","result":{"status":{}}}""")]
    [TestCase("""{"jsonrpc":"2.0","id":"1","result":{"status":{"editorSession":{"name":7}}}}""")]
    public async Task PreflightRejectsMissingOrNonStringIdentity(string response)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(response));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client, new Uri("http://localhost:5467/mcp/"), new AgentToolPolicy());

        AgentToolProviderException exception = await Should.ThrowAsync<AgentToolProviderException>(
            () => provider.PreflightAsync("expected", CancellationToken.None,
                ["status", "editorSession", "name"]));

        exception.Category.ShouldBe(AgentFailureCategory.ToolDiscovery);
    }

    [Test]
    public async Task PreflightWithoutIdentityPathChecksOnlyLiveness()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """{"jsonrpc":"2.0","id":"1","result":{"status":{"editorSession":{"name":"other"}}}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client, new Uri("http://localhost:5467/mcp/"), new AgentToolPolicy());

        string? identity = await provider.PreflightAsync("expected", CancellationToken.None);

        identity.ShouldBeNull();
        handler.RequestBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task UnannotatedToolNameDoesNotGrantReadOrMutationAccess()
    {
        const string response =
            """{"jsonrpc":"2.0","id":"1","result":{"tools":[{"name":"read_data","inputSchema":{"type":"object"}}]}}""";
        foreach (AgentToolPolicy policy in new[]
        {
            new AgentToolPolicy(),
            new AgentToolPolicy { AllowMutation = true, AllowedTools = ["read_data"] },
        })
        {
            var handler = new QueueHttpMessageHandler();
            handler.Enqueue(_ => JsonResponse(response));
            using var client = new HttpClient(handler);
            var provider = new HttpMcpToolProvider(
                client, new Uri("http://localhost:5467/mcp/"), policy);

            IReadOnlyList<AgentToolDefinition> tools = await provider.ListToolsAsync(CancellationToken.None);

            tools.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task UnannotatedToolRequiresDestructivePermissionAndExactAllowlist()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """{"jsonrpc":"2.0","id":"1","result":{"tools":[{"name":"read_data","inputSchema":{"type":"object"}}]}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client,
            new Uri("http://localhost:5467/mcp/"),
            new AgentToolPolicy
            {
                AllowMutation = true,
                AllowDestructive = true,
                AllowedTools = ["read_data"],
            });

        IReadOnlyList<AgentToolDefinition> tools = await provider.ListToolsAsync(CancellationToken.None);

        tools.Select(static tool => tool.Name).ShouldBe(["read_data"]);
        tools[0].IsReadOnly.ShouldBeFalse();
        tools[0].IsDestructive.ShouldBeTrue();
    }

    [Test]
    public async Task MutatingToolWithoutExplicitNonDestructiveHintRequiresDestructivePermission()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """{"jsonrpc":"2.0","id":"1","result":{"tools":[{"name":"set_data","annotations":{"readOnlyHint":false},"inputSchema":{"type":"object"}}]}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client,
            new Uri("http://localhost:5467/mcp/"),
            new AgentToolPolicy { AllowMutation = true, AllowedTools = ["set_data"] });

        IReadOnlyList<AgentToolDefinition> tools = await provider.ListToolsAsync(CancellationToken.None);

        tools.ShouldBeEmpty();
    }

    [Test]
    public async Task ExplicitDestructiveHintBlocksReadOnlyAccess()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """{"jsonrpc":"2.0","id":"1","result":{"tools":[{"name":"get_data","annotations":{"readOnlyHint":true,"destructiveHint":true},"inputSchema":{"type":"object"}}]}}"""));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client, new Uri("http://localhost:5467/mcp/"), new AgentToolPolicy());

        IReadOnlyList<AgentToolDefinition> tools = await provider.ListToolsAsync(CancellationToken.None);

        tools.ShouldBeEmpty();
    }

    [Test]
    public async Task MalformedToolArgumentsRemainAnErrorWithoutCallingEditor()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(_ => JsonResponse(
            """
            {"jsonrpc":"2.0","id":"1","result":{"tools":[{"name":"get_node","annotations":{"readOnlyHint":true},"inputSchema":{"type":"object"}}]}}
            """));
        using var client = new HttpClient(handler);
        var provider = new HttpMcpToolProvider(
            client,
            new Uri("http://127.0.0.1:5467/mcp/"),
            new AgentToolPolicy());
        await provider.ListToolsAsync(CancellationToken.None);

        AgentToolResult result = await provider.ExecuteAsync(
            new AgentToolCall
            {
                CallId = "malformed",
                Name = "get_node",
                ArgumentsJson = "{not-json",
            },
            CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.Content.ShouldContain("not valid JSON");
        handler.RequestBodies.Count.ShouldBe(1);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
