using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Coach.Api.Tests;

public sealed class ProfileTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Mcp_requires_authentication()
    {
        await using var host = new CoachHost(database);
        using var client = host.CreateClient();
        using var response = await client.PostAsync("/mcp", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_profile_then_ingested_body_weight_is_read_through_mcp()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var empty = await CallTool(client, "get_athlete_profile");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastSyncedAt").ValueKind);

        using var response = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[] { new { bodyWeightKg = 82.5m } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await CallTool(client, "get_athlete_profile");
        Assert.Equal(82.5m, saved.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.True(saved.GetProperty("lastSyncedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Update_tool_creates_and_updates_weight_and_it_survives_host_recreation()
    {
        var userId = Guid.NewGuid().ToString();
        JsonElement saved;
        await using (var host = new CoachHost(database))
        {
            using var client = AthleteClient(host, userId);
            var created = await CallTool(client, "update_athlete_profile", new { bodyWeightKg = 90.1m });
            Assert.Equal(90.1m, created.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
            saved = await CallTool(client, "update_athlete_profile", new { bodyWeightKg = 89.5m });
            Assert.Equal(89.5m, saved.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        }
        await using var recreated = new CoachHost(database);
        using var reader = AthleteClient(recreated, userId);
        var persisted = await CallTool(reader, "get_athlete_profile");
        Assert.Equal(89.5m, persisted.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal(userId, persisted.GetProperty("profile").GetProperty("userId").GetString());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), persisted.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/ingest")]
    public async Task Both_boundaries_reject_invalid_tokens_and_publish_a_resource_challenge(string path)
    {
        await using var host = new CoachHost(database);
        await using var otherIssuerKey = new CoachHost(database);
        using var client = host.CreateClient();
        string?[] tokens = [null, "invalid", host.Token("athlete", "https://wrong.example"),
            otherIssuerKey.Token("athlete"), host.Token("athlete", expires: DateTime.UtcNow.AddMinutes(-2)),
            host.Token("athlete", issuer: "https://wrong-issuer.example"), host.Token(null), host.Token("athlete ")];
        foreach (var token in tokens)
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("resource_metadata=\"https://coach.example/.well-known/oauth-protected-resource\"",
                response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        }
        using var metadata = await client.GetAsync("/.well-known/oauth-protected-resource");
        metadata.EnsureSuccessStatusCode();
        var document = await metadata.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CoachHost.Audience, document.GetProperty("resource").GetString());
        Assert.Equal(CoachHost.Issuer, document.GetProperty("authorization_servers")[0].GetString());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Athletes_are_isolated_and_supplied_identifiers_cannot_select_another_profile()
    {
        await using var host = new CoachHost(database);
        var firstId = $"Athlete-{Guid.NewGuid()}";
        var secondId = firstId.ToLowerInvariant();
        using var first = AthleteClient(host, firstId);
        using var second = AthleteClient(host, secondId);
        using var seeded = await first.PostAsJsonAsync("/ingest", new { version = 1, athleteProfiles = new[] { new { bodyWeightKg = 70m } } });
        seeded.EnsureSuccessStatusCode();
        var empty = await CallTool(second, "get_athlete_profile", new { userId = firstId });
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
        var ownUpdate = await CallTool(second, "update_athlete_profile", new { bodyWeightKg = 91m, userId = firstId });
        Assert.Equal(secondId, ownUpdate.GetProperty("profile").GetProperty("userId").GetString());
        using var forged = await second.PostAsJsonAsync("/ingest", new
        {
            version = 1, athleteProfiles = new[] { new { userId = firstId, bodyWeightKg = 10m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        var unchanged = await CallTool(first, "get_athlete_profile");
        Assert.Equal(70m, unchanged.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        var own = await CallTool(second, "get_athlete_profile");
        Assert.Equal(91m, own.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
    }

    [Theory]
    [InlineData("{\"version\":2,\"athleteProfiles\":[{\"bodyWeightKg\":65}]}")]
    [InlineData("{\"athleteProfiles\":[{\"bodyWeightKg\":65}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":null}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[null]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":65},{\"bodyWeightKg\":-1}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":0}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":65.1234}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":65.12}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":100000000000000000}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":\"bad\"}]}")]
    [InlineData("{\"version\":1,\"userId\":\"another-athlete\",\"athleteProfiles\":[{\"bodyWeightKg\":65}]}")]
    [InlineData("null")]
    [InlineData("{broken")]
    public async Task Invalid_ingest_batches_leave_the_saved_profile_and_freshness_unchanged(string batch)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new { version = 1, athleteProfiles = new[] { new { bodyWeightKg = 80m } } });
        seed.EnsureSuccessStatusCode();
        var before = await CallTool(client, "get_athlete_profile");
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var after = await CallTool(client, "get_athlete_profile");
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public async Task Exactly_two_tools_are_discoverable_after_initialization_without_a_session()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var initialized = await Rpc(client, "initialize", new
        {
            protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "boundary-tests", version = "1" }
        });
        Assert.Equal("2025-11-25", initialized.GetProperty("result").GetProperty("protocolVersion").GetString());
        var discovery = await Rpc(client, "tools/list", new { });
        var tools = discovery.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);
        var read = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "get_athlete_profile");
        var update = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "update_athlete_profile");
        Assert.True(read.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.False(update.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.Equal("number", update.GetProperty("inputSchema").GetProperty("properties").GetProperty("bodyWeightKg").GetProperty("type").GetString());
        Assert.Contains(update.GetProperty("inputSchema").GetProperty("required").EnumerateArray(), item => item.GetString() == "bodyWeightKg");
        Assert.False(update.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("userId", out _));
        await CallTool(client, "get_athlete_profile");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"bodyWeightKg\":\"not-a-number\"}")]
    [InlineData("{\"bodyWeightKg\":0}")]
    [InlineData("{\"bodyWeightKg\":-5}")]
    [InlineData("{\"bodyWeightKg\":70.1234}")]
    [InlineData("{\"bodyWeightKg\":70.12}")]
    public async Task Invalid_tool_arguments_do_not_create_a_profile(string arguments)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var result = await Rpc(client, "tools/call", new { name = "update_athlete_profile", arguments = JsonDocument.Parse(arguments).RootElement });
        Assert.True(result.TryGetProperty("error", out _) || result.GetProperty("result").GetProperty("isError").GetBoolean());
        var empty = await CallTool(client, "get_athlete_profile");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastSyncedAt").ValueKind);
    }

    [Fact]
    public async Task Browser_clients_can_preflight_the_mcp_endpoint()
    {
        await using var host = new CoachHost(database);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp");
        request.Headers.Add("Origin", "https://chat.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,mcp-protocol-version");
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task An_update_cannot_bypass_ingest_authorization()
    {
        await using var host = new CoachHost(database) { DenyIngest = true };
        using var client = AthleteClient(host);
        var rejected = await Rpc(client, "tools/call", new { name = "update_athlete_profile", arguments = new { bodyWeightKg = 72m } });
        Assert.True(rejected.GetProperty("result").GetProperty("isError").GetBoolean());
        var empty = await CallTool(client, "get_athlete_profile");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
    }

    [Fact]
    public async Task Current_protocol_clients_can_discover_and_call_without_initializing()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var discovery = await Rpc(client, "server/discover", new { }, "2026-07-28");
        Assert.True(discovery.TryGetProperty("result", out _), discovery.ToString());
        var read = await Rpc(client, "tools/call", new { name = "get_athlete_profile", arguments = new { } }, "2026-07-28");
        Assert.Equal(JsonValueKind.Null, read.GetProperty("result").GetProperty("structuredContent").GetProperty("profile").ValueKind);
    }

    [Fact]
    public async Task A_valid_batch_applies_changes_in_order_and_returns_the_saved_facts()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var response = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1, athleteProfiles = new[] { new { bodyWeightKg = 78m }, new { bodyWeightKg = 77.1m } }
        });
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var read = await CallTool(client, "get_athlete_profile");
        Assert.Equal(77.1m, saved.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal(77.1m, read.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), read.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    private static HttpClient AthleteClient(CoachHost host, string? userId = null)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token(userId ?? Guid.NewGuid().ToString()));
        return client;
    }

    private static async Task<JsonElement> CallTool(HttpClient client, string name, object? arguments = null)
    {
        var rpc = await Rpc(client, "tools/call", new { name, arguments = arguments ?? new { } });
        var result = rpc.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.ToString());
        Assert.Empty(result.GetProperty("content").EnumerateArray());
        return result.GetProperty("structuredContent");
    }

    private static async Task<JsonElement> Rpc(HttpClient client, string method, object parameters, string protocolVersion = "2025-11-25")
    {
        var rpcParameters = JsonSerializer.SerializeToNode(parameters)!;
        if (protocolVersion == "2026-07-28")
            rpcParameters["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = protocolVersion,
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject()
            };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = rpcParameters })
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        if (protocolVersion == "2026-07-28")
        {
            request.Headers.Add("Mcp-Method", method);
            if (method == "tools/call")
                request.Headers.Add("Mcp-Name", rpcParameters["name"]!.GetValue<string>());
        }
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = body.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
