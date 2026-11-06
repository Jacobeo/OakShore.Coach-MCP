using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using OakShore.Coach.Infrastructure;
using Xunit;

namespace OakShore.Coach.Api.Tests;

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
    [InlineData("{\"version\":3,\"athleteProfiles\":[{\"bodyWeightKg\":65}]}")]
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
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"goals\":[{\"description\":\"Football\"},{\"description\":\"football\"}]}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"goals\":[],\"goalsTargetDate\":\"2099-04-01\"}]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"goal\":{\"description\":\"Football\",\"targetDate\":\"2099-04-01\"},\"goals\":[{\"description\":\"VO2max\"}]}]}")]
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
    public async Task Read_tools_are_discoverable_after_initialization_without_a_session()
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
        Assert.Equal(3, tools.Length);
        var read = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "get_athlete_profile");
        var update = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "update_athlete_profile");
        var syncStatus = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "get_sync_status");
        Assert.True(read.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.True(syncStatus.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.False(update.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        Assert.Contains(update.GetProperty("inputSchema").GetProperty("properties").GetProperty("bodyWeightKg").GetProperty("type").EnumerateArray(),
            item => item.GetString() == "number");
        var properties = update.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(properties.TryGetProperty("goals", out _));
        Assert.True(properties.TryGetProperty("goalsTargetDate", out _));
        Assert.False(properties.TryGetProperty("goal", out _));
        Assert.False(update.GetProperty("inputSchema").TryGetProperty("required", out _));
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

    [Fact]
    public async Task Stable_facts_and_legacy_goal_are_saved_through_ingest_and_read_through_mcp()
    {
        var userId = Guid.NewGuid().ToString();
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host, userId);
        using var response = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[]
            {
                new
                {
                    availableEquipment = new[] { "Full gym", "Stationary bike" },
                    intendedTrainingFrequencyPerWeek = 3,
                    intendedTrainingDurationMinutes = 60,
                    lastingLimitations = new[] { "Weak ankles and feet" },
                    goal = new { description = "Play 7-a-side football", targetDate = "2099-04-01" }
                }
            }
        });
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var read = await CallTool(client, "get_athlete_profile");
        var profile = read.GetProperty("profile");
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("bodyWeightKg").ValueKind);
        Assert.Equal(new[] { "Full gym", "Stationary bike" }, profile.GetProperty("availableEquipment").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(3, profile.GetProperty("intendedTrainingFrequencyPerWeek").GetInt32());
        Assert.Equal(60, profile.GetProperty("intendedTrainingDurationMinutes").GetInt32());
        Assert.Equal("Weak ankles and feet", profile.GetProperty("lastingLimitations")[0].GetString());
        Assert.Equal("Play 7-a-side football", profile.GetProperty("goals")[0].GetProperty("description").GetString());
        Assert.Equal("2099-04-01", profile.GetProperty("goalsTargetDate").GetString());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), read.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Goals_are_saved_and_read_in_priority_order()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[] { new { bodyWeightKg = 80m } }
        });
        seed.EnsureSuccessStatusCode();
        var saved = await CallTool(client, "update_athlete_profile", new
        {
            goals = new[]
            {
                new { description = "Play 7-a-side football" },
                new { description = "Improve VO2max" }
            }
        });
        var goals = saved.GetProperty("profile").GetProperty("goals").EnumerateArray()
            .Select(goal => goal.GetProperty("description").GetString()).ToArray();
        Assert.Equal(new[] { "Play 7-a-side football", "Improve VO2max" }, goals);
        var read = await CallTool(client, "get_athlete_profile");
        Assert.Equal(saved.GetProperty("profile").ToString(), read.GetProperty("profile").ToString());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), read.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Reordering_goals_preserves_the_shared_date_and_clearing_goals_clears_the_date()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[]
            {
                new
                {
                    goals = new[] { new { description = "Football" }, new { description = "VO2max" } },
                    goalsTargetDate = "2099-04-01",
                    bodyWeightKg = 80m
                }
            }
        });
        seed.EnsureSuccessStatusCode();
        var reordered = await CallTool(client, "update_athlete_profile", new
        {
            goals = new[] { new { description = "VO2max" }, new { description = "Football" } }
        });
        var profile = reordered.GetProperty("profile");
        Assert.Equal("VO2max", profile.GetProperty("goals")[0].GetProperty("description").GetString());
        Assert.Equal("Football", profile.GetProperty("goals")[1].GetProperty("description").GetString());
        Assert.Equal("2099-04-01", profile.GetProperty("goalsTargetDate").GetString());
        Assert.Equal(80m, profile.GetProperty("bodyWeightKg").GetDecimal());
        var newDate = await CallTool(client, "update_athlete_profile", new { goalsTargetDate = "2099-05-01" });
        Assert.Equal("VO2max", newDate.GetProperty("profile").GetProperty("goals")[0].GetProperty("description").GetString());
        Assert.Equal("2099-05-01", newDate.GetProperty("profile").GetProperty("goalsTargetDate").GetString());
        var cleared = await CallTool(client, "update_athlete_profile", new { goals = Array.Empty<object>() });
        Assert.Empty(cleared.GetProperty("profile").GetProperty("goals").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("profile").GetProperty("goalsTargetDate").ValueKind);
        var read = await CallTool(client, "get_athlete_profile");
        Assert.Equal(cleared.GetProperty("profile").ToString(), read.GetProperty("profile").ToString());
    }

    [Fact]
    public async Task Existing_single_goal_survives_the_prioritized_goals_migration()
    {
        var legacy = new DatabaseFixture();
        await legacy.InitializeAsync();
        try
        {
            // A pre-upgrade schema cannot be seeded through ingest because the host migrates before serving requests.
            await using (var connection = new SqlConnection(legacy.ConnectionString))
            {
                await connection.OpenAsync();
                foreach (var resource in new[]
                {
                    "OakShore.Coach.Infrastructure.Migrations.0001_InitialAthleteProfile.sql",
                    "OakShore.Coach.Infrastructure.Migrations.0002_StableFactsAndGoal.sql"
                })
                {
                    await using var stream = typeof(AthleteProfileStore).Assembly.GetManifestResourceStream(resource)!;
                    using var reader = new StreamReader(stream);
                    await using var command = new SqlCommand(await reader.ReadToEndAsync(), connection);
                    await command.ExecuteNonQueryAsync();
                }
                await using var seed = new SqlCommand("INSERT dbo.AthleteProfiles (UserId, BodyWeightKg, LastSyncedAt, GoalDescription, GoalTargetDate) VALUES (@userId, 80, SYSDATETIMEOFFSET(), N'Football', '2099-04-01')", connection);
                var userId = Guid.NewGuid().ToString();
                seed.Parameters.AddWithValue("@userId", userId);
                await seed.ExecuteNonQueryAsync();
                await using var host = new CoachHost(legacy);
                using var client = AthleteClient(host, userId);
                var read = await CallTool(client, "get_athlete_profile");
                Assert.Equal("Football", read.GetProperty("profile").GetProperty("goals")[0].GetProperty("description").GetString());
                Assert.Equal("2099-04-01", read.GetProperty("profile").GetProperty("goalsTargetDate").GetString());
                Assert.Equal(80m, read.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
            }
        }
        finally
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tool_updates_each_stable_fact_without_erasing_unmentioned_facts()
    {
        var userId = Guid.NewGuid().ToString();
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host, userId);
        using var seed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[] { new { bodyWeightKg = 80m, availableEquipment = new[] { "Full gym" } } }
        });
        seed.EnsureSuccessStatusCode();
        var frequency = await CallTool(client, "update_athlete_profile", new { intendedTrainingFrequencyPerWeek = 3 });
        Assert.Equal(80m, frequency.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        var duration = await CallTool(client, "update_athlete_profile", new { intendedTrainingDurationMinutes = 60 });
        Assert.Equal(3, duration.GetProperty("profile").GetProperty("intendedTrainingFrequencyPerWeek").GetInt32());
        var limitations = await CallTool(client, "update_athlete_profile", new { lastingLimitations = new[] { "Weak ankles" } });
        Assert.Equal("Weak ankles", limitations.GetProperty("profile").GetProperty("lastingLimitations")[0].GetString());
        var goals = await CallTool(client, "update_athlete_profile", new
        {
            goals = new[] { new { description = "Football season" } }, goalsTargetDate = "2099-04-01"
        });
        Assert.Equal("Football season", goals.GetProperty("profile").GetProperty("goals")[0].GetProperty("description").GetString());
        var equipment = await CallTool(client, "update_athlete_profile", new { availableEquipment = new[] { "Bike" } });
        Assert.Equal("Bike", equipment.GetProperty("profile").GetProperty("availableEquipment")[0].GetString());
        var cleared = await CallTool(client, "update_athlete_profile", new { lastingLimitations = Array.Empty<string>() });
        Assert.Empty(cleared.GetProperty("profile").GetProperty("lastingLimitations").EnumerateArray());
        var read = await CallTool(client, "get_athlete_profile");
        Assert.Equal(80m, read.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal(3, read.GetProperty("profile").GetProperty("intendedTrainingFrequencyPerWeek").GetInt32());
        Assert.Equal(60, read.GetProperty("profile").GetProperty("intendedTrainingDurationMinutes").GetInt32());
        Assert.Equal("Football season", read.GetProperty("profile").GetProperty("goals")[0].GetProperty("description").GetString());
        Assert.Equal("2099-04-01", read.GetProperty("profile").GetProperty("goalsTargetDate").GetString());
        Assert.Equal(cleared.GetProperty("lastSyncedAt").GetDateTimeOffset(), read.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("{\"intendedTrainingFrequencyPerWeek\":0}", "frequency")]
    [InlineData("{\"intendedTrainingDurationMinutes\":1441}", "duration")]
    [InlineData("{\"availableEquipment\":[\"  \"]}", "equipment")]
    [InlineData("{\"lastingLimitations\":[null]}", "limitations")]
    [InlineData("{\"goalsTargetDate\":\"2020-01-01\"}", "date")]
    [InlineData("{\"goalsTargetDate\":\"2099-02-30\"}", "date")]
    [InlineData("{\"goals\":[{\"description\":\" \"}]}", "description")]
    [InlineData("{\"goals\":[{\"description\":\"Football\"},{\"description\":\"football\"}]}", "distinct")]
    [InlineData("{\"goals\":[null]}", "description")]
    public async Task Invalid_stable_fact_tool_update_reports_error_and_preserves_profile(string arguments, string field)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new { version = 1, athleteProfiles = new[] { new { bodyWeightKg = 80m } } });
        seed.EnsureSuccessStatusCode();
        var before = await CallTool(client, "get_athlete_profile");
        var result = await Rpc(client, "tools/call", new { name = "update_athlete_profile", arguments = JsonDocument.Parse(arguments).RootElement });
        var error = result.GetProperty("result");
        Assert.True(error.GetProperty("isError").GetBoolean());
        Assert.Empty(error.GetProperty("content").EnumerateArray());
        Assert.Contains(field, error.GetProperty("structuredContent").GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        var after = await CallTool(client, "get_athlete_profile");
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public async Task Invalid_later_ingest_change_rejects_the_whole_batch()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new { version = 1, athleteProfiles = new[] { new { bodyWeightKg = 80m } } });
        seed.EnsureSuccessStatusCode();
        var before = await CallTool(client, "get_athlete_profile");
        using var rejected = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new object[]
            {
                new { availableEquipment = new[] { "Bike" } },
                new { goal = new { description = "Football", targetDate = "2020-01-01" } }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var feedback = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("date", feedback.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        var after = await CallTool(client, "get_athlete_profile");
        Assert.Equal(before.ToString(), after.ToString());
    }

    [Fact]
    public async Task Goal_date_rejected_at_ingest_still_returns_structured_tool_feedback()
    {
        await using var host = new CoachHost(database)
        {
            Clock = new AdvancingClock(
                new DateTimeOffset(2099, 1, 1, 23, 59, 59, TimeSpan.Zero),
                new DateTimeOffset(2099, 1, 2, 0, 0, 0, TimeSpan.Zero))
        };
        using var client = AthleteClient(host);
        var result = await Rpc(client, "tools/call", new
        {
            name = "update_athlete_profile",
            arguments = new { goalsTargetDate = "2099-01-01" }
        });
        var error = result.GetProperty("result");
        Assert.True(error.GetProperty("isError").GetBoolean());
        Assert.Empty(error.GetProperty("content").EnumerateArray());
        Assert.Contains("date", error.GetProperty("structuredContent").GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        var empty = await CallTool(client, "get_athlete_profile");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
    }

    private sealed class AdvancingClock(DateTimeOffset first, DateTimeOffset after) : TimeProvider
    {
        private int calls;
        public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref calls) == 1 ? first : after;
    }

    [Fact]
    public async Task Stable_facts_are_isolated_between_user_ids()
    {
        await using var host = new CoachHost(database);
        using var first = AthleteClient(host);
        using var second = AthleteClient(host);
        var saved = await CallTool(first, "update_athlete_profile", new
        {
            availableEquipment = new[] { "Full gym" },
            goals = new[] { new { description = "Football" } }, goalsTargetDate = "2099-04-01"
        });
        Assert.Equal("Full gym", saved.GetProperty("profile").GetProperty("availableEquipment")[0].GetString());
        var empty = await CallTool(second, "get_athlete_profile");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("profile").ValueKind);
        await CallTool(second, "update_athlete_profile", new { lastingLimitations = new[] { "Weak ankles" } });
        var firstRead = await CallTool(first, "get_athlete_profile");
        Assert.Empty(firstRead.GetProperty("profile").GetProperty("lastingLimitations").EnumerateArray());
        var secondRead = await CallTool(second, "get_athlete_profile");
        Assert.Empty(secondRead.GetProperty("profile").GetProperty("goals").EnumerateArray());
    }

    [Fact]
    public async Task Stable_facts_survive_host_recreation()
    {
        var userId = Guid.NewGuid().ToString();
        JsonElement saved;
        await using (var host = new CoachHost(database))
        {
            using var client = AthleteClient(host, userId);
            saved = await CallTool(client, "update_athlete_profile", new
            {
                availableEquipment = new[] { "Bike" },
                intendedTrainingFrequencyPerWeek = 3,
                intendedTrainingDurationMinutes = 60,
                lastingLimitations = new[] { "Weak ankles" },
                goals = new[] { new { description = "Football" }, new { description = "Improve VO2max" } },
                goalsTargetDate = "2099-04-01"
            });
        }
        await using var recreated = new CoachHost(database);
        using var reader = AthleteClient(recreated, userId);
        var persisted = await CallTool(reader, "get_athlete_profile");
        Assert.Equal(saved.GetProperty("profile").ToString(), persisted.GetProperty("profile").ToString());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), persisted.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Constraint_update_appends_to_profile_without_erasing_existing_facts()
    {
        var now = new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero);
        await using var host = new CoachHost(database) { Clock = new FixedClock(now) };
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[] { new { bodyWeightKg = 80m, availableEquipment = new[] { "Bike" } } }
        });
        seed.EnsureSuccessStatusCode();

        var saved = await CallTool(client, "update_athlete_profile", new
        {
            constraint = new
            {
                description = "Avoid running while ankle heals",
                validFrom = "2030-04-10T00:00:00Z",
                validUntil = "2030-04-24T00:00:00Z"
            }
        });
        var profile = saved.GetProperty("profile");
        Assert.Equal(80m, profile.GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal("Bike", profile.GetProperty("availableEquipment")[0].GetString());
        var constraint = Assert.Single(profile.GetProperty("constraints").EnumerateArray());
        Assert.Equal("Avoid running while ankle heals", constraint.GetProperty("description").GetString());
        Assert.Equal("2030-04-10T00:00:00+00:00", constraint.GetProperty("validFrom").GetString());
        Assert.Equal("2030-04-24T00:00:00+00:00", constraint.GetProperty("validUntil").GetString());

        var read = await CallTool(client, "get_athlete_profile");
        Assert.Equal(profile.ToString(), read.GetProperty("profile").ToString());
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), read.GetProperty("lastSyncedAt").GetDateTimeOffset());

        var second = await CallTool(client, "update_athlete_profile", new
        {
            constraint = new { description = "Short sessions this week", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-17T00:00:00Z" }
        });
        Assert.Equal(new[] { "Avoid running while ankle heals", "Short sessions this week" }, ConstraintDescriptions(second));
        Assert.Equal(80m, second.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Constraint_reads_filter_by_current_time_without_deleting_history_or_changing_freshness()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 0, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new object[]
            {
                new { bodyWeightKg = 80m },
                new { constraint = new { description = "Expired", validFrom = "2030-04-08T00:00:00Z", validUntil = "2030-04-10T00:00:00Z" } },
                new { constraint = new { description = "Active", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-11T00:00:00Z" } },
                new { constraint = new { description = "Future", validFrom = "2030-04-11T00:00:00Z", validUntil = "2030-04-12T00:00:00Z" } }
            }
        });
        seed.EnsureSuccessStatusCode();
        var atStart = await CallTool(client, "get_athlete_profile");
        Assert.Equal(new[] { "Active" }, ConstraintDescriptions(atStart));
        var freshness = atStart.GetProperty("lastSyncedAt").GetDateTimeOffset();

        clock.Set(new DateTimeOffset(2030, 4, 11, 0, 0, 0, TimeSpan.Zero));
        var atEnd = await CallTool(client, "get_athlete_profile");
        Assert.Equal(new[] { "Future" }, ConstraintDescriptions(atEnd));
        Assert.Equal(80m, atEnd.GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());
        Assert.Equal(freshness, atEnd.GetProperty("lastSyncedAt").GetDateTimeOffset());

        clock.Set(new DateTimeOffset(2030, 4, 12, 0, 0, 0, TimeSpan.Zero));
        var allExpired = await CallTool(client, "get_athlete_profile");
        Assert.Empty(ConstraintDescriptions(allExpired));
        Assert.Equal(freshness, allExpired.GetProperty("lastSyncedAt").GetDateTimeOffset());

        clock.Set(new DateTimeOffset(2030, 4, 9, 12, 0, 0, TimeSpan.Zero));
        var historical = await CallTool(client, "get_athlete_profile");
        Assert.Equal(new[] { "Expired" }, ConstraintDescriptions(historical));
        Assert.Equal(freshness, historical.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Future_constraint_is_confirmed_by_update_but_not_read_before_its_start()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var saved = await CallTool(client, "update_athlete_profile", new
        {
            constraint = new { description = "Travel week", validFrom = "2030-04-11T00:00:00Z", validUntil = "2030-04-18T00:00:00Z" }
        });
        Assert.Empty(ConstraintDescriptions(saved));
        var confirmed = Assert.Single(saved.GetProperty("savedConstraints").EnumerateArray());
        Assert.Equal("Travel week", confirmed.GetProperty("description").GetString());
        Assert.Equal("2030-04-11T00:00:00+00:00", confirmed.GetProperty("validFrom").GetString());
        Assert.Equal("2030-04-18T00:00:00+00:00", confirmed.GetProperty("validUntil").GetString());

        var before = await CallTool(client, "get_athlete_profile");
        Assert.Empty(ConstraintDescriptions(before));
        clock.Set(new DateTimeOffset(2030, 4, 11, 0, 0, 0, TimeSpan.Zero));
        var atStart = await CallTool(client, "get_athlete_profile");
        Assert.Equal(new[] { "Travel week" }, ConstraintDescriptions(atStart));
        Assert.Equal(saved.GetProperty("lastSyncedAt").GetDateTimeOffset(), atStart.GetProperty("lastSyncedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("{\"description\":\"Running\",\"validFrom\":\"2030-04-10T00:00:00Z\",\"validUntil\":\"2030-04-10T00:00:00Z\"}")]
    [InlineData("{\"description\":\"Running\",\"validFrom\":\"2030-04-11T00:00:00Z\",\"validUntil\":\"2030-04-10T00:00:00Z\"}")]
    [InlineData("{\"description\":\"Running\",\"validFrom\":\"2030-04-10T00:00:00+02:00\",\"validUntil\":\"2030-04-11T00:00:00Z\"}")]
    [InlineData("{\"description\":\"Running\",\"validFrom\":\"2030-04-10\",\"validUntil\":\"2030-04-11T00:00:00Z\"}")]
    [InlineData("{\"description\":\" \",\"validFrom\":\"2030-04-10T00:00:00Z\",\"validUntil\":\"2030-04-11T00:00:00Z\"}")]
    public async Task Invalid_constraint_period_or_description_is_rejected_without_saving(string constraintJson)
    {
        await using var host = new CoachHost(database) { Clock = new FixedClock(new DateTimeOffset(2030, 4, 10, 0, 0, 0, TimeSpan.Zero)) };
        using var client = AthleteClient(host);
        using var seed = await client.PostAsJsonAsync("/ingest", new { version = 1, athleteProfiles = new[] { new { bodyWeightKg = 80m } } });
        seed.EnsureSuccessStatusCode();
        var before = await CallTool(client, "get_athlete_profile");
        var constraint = JsonDocument.Parse(constraintJson).RootElement;
        var result = await Rpc(client, "tools/call", new { name = "update_athlete_profile", arguments = new { constraint } });
        Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Empty(result.GetProperty("result").GetProperty("content").EnumerateArray());
        Assert.Equal(before.ToString(), (await CallTool(client, "get_athlete_profile")).ToString());

        using var rejected = await client.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new object[] { new { bodyWeightKg = 81m }, new { constraint } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_athlete_profile")).ToString());
    }

    [Fact]
    public async Task Constraints_are_scoped_to_authenticated_user_id()
    {
        await using var host = new CoachHost(database) { Clock = new FixedClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero)) };
        var firstId = $"Athlete-{Guid.NewGuid()}";
        using var first = AthleteClient(host, firstId);
        using var second = AthleteClient(host, firstId.ToLowerInvariant());
        var saved = await CallTool(first, "update_athlete_profile", new
        {
            constraint = new { description = "First athlete", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-11T00:00:00Z" }
        });
        Assert.Equal(new[] { "First athlete" }, ConstraintDescriptions(saved));
        Assert.Equal(JsonValueKind.Null, (await CallTool(second, "get_athlete_profile", new { userId = firstId })).GetProperty("profile").ValueKind);

        var own = await CallTool(second, "update_athlete_profile", new
        {
            userId = firstId,
            constraint = new { description = "Second athlete", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-11T00:00:00Z" }
        });
        Assert.Equal(new[] { "Second athlete" }, ConstraintDescriptions(own));
        Assert.Equal(new[] { "First athlete" }, ConstraintDescriptions(await CallTool(first, "get_athlete_profile")));
        using var forged = await second.PostAsJsonAsync("/ingest", new
        {
            version = 1,
            athleteProfiles = new[] { new { userId = firstId, constraint = new { description = "Forged", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-11T00:00:00Z" } } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(new[] { "First athlete" }, ConstraintDescriptions(await CallTool(first, "get_athlete_profile")));
    }

    private static string?[] ConstraintDescriptions(JsonElement response) =>
        response.GetProperty("profile").GetProperty("constraints").EnumerateArray()
            .Select(item => item.GetProperty("description").GetString()).ToArray();

    [Fact]
    public async Task Version_two_ingests_fixture_activities_and_reports_collection_freshness()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        Assert.Equal(0, Collection(before, "Activity").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, Collection(before, "Activity").GetProperty("lastSyncedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("latestActivity").ValueKind);
        Assert.Equal(0, Collection(before, "AthleteProfile").GetProperty("count").GetInt32());

        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../fixtures/garmin/activitylist-service--search-activities__page-00.json"))));
        var activities = fixture.RootElement.EnumerateArray().Select(activity => new
        {
            activityId = activity.GetProperty("activityId").GetInt64(),
            activityName = activity.GetProperty("activityName").GetString(),
            startTimeUtc = activity.GetProperty("startTimeGMT").GetString()!.Replace(' ', 'T') + "Z",
            typeKey = activity.GetProperty("activityType").GetProperty("typeKey").GetString(),
            durationSeconds = activity.GetProperty("duration").GetDecimal(),
            distanceMeters = activity.GetProperty("distance").GetDecimal(),
            totalSets = activity.TryGetProperty("totalSets", out var sets) ? sets.GetInt32() : (int?)null,
            activeSets = activity.TryGetProperty("activeSets", out var active) ? active.GetInt32() : (int?)null,
            totalReps = activity.TryGetProperty("totalReps", out var reps) ? reps.GetInt32() : (int?)null
        }).ToArray();
        var batch = new
        {
            version = 2,
            athleteProfiles = new[] { new { bodyWeightKg = 82.5m } },
            activityRange = new { fromDate = "2026-08-05", toDate = "2026-09-22" },
            activities
        };
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var accepted = await posted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(clock.GetUtcNow(), accepted.GetProperty("lastSyncedAt").GetDateTimeOffset());
        var saved = await CallTool(client, "get_sync_status");
        Assert.Equal(15, Collection(saved, "Activity").GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), Collection(saved, "Activity").GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(1, Collection(saved, "AthleteProfile").GetProperty("count").GetInt32());
        Assert.Equal(24444892080L, saved.GetProperty("latestActivity").GetProperty("activityId").GetInt64());
        Assert.Equal("strength_training", saved.GetProperty("latestActivity").GetProperty("activityName").GetString());
        Assert.Equal("strength_training", saved.GetProperty("latestActivity").GetProperty("typeKey").GetString());
        Assert.Equal(2820.37890625, saved.GetProperty("latestActivity").GetProperty("durationSeconds").GetDouble());
        Assert.Equal(14, saved.GetProperty("latestActivity").GetProperty("totalSets").GetInt32());

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var reposted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, reposted.StatusCode);
        Assert.Equal(saved.ToString(), (await CallTool(client, "get_sync_status")).ToString());
        Assert.Equal(82.5m, (await CallTool(client, "get_athlete_profile")).GetProperty("profile").GetProperty("bodyWeightKg").GetDecimal());

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var corrected = await client.PostAsJsonAsync("/ingest", new
        {
            version = 2,
            activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-22" },
            activities = new[] { new { activityId = 24444892080L, startTimeUtc = "2026-09-21T14:34:01Z",
                activityName = "Sunday strength", typeKey = "strength_training", durationSeconds = 3000.0, distanceMeters = 0.0,
                totalSets = 14, activeSets = 14, totalReps = 82 } }
        });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var updated = await CallTool(client, "get_sync_status");
        Assert.Equal(15, Collection(updated, "Activity").GetProperty("count").GetInt32());
        Assert.Equal(3000.0, updated.GetProperty("latestActivity").GetProperty("durationSeconds").GetDouble());
        Assert.Equal("Sunday strength", updated.GetProperty("latestActivity").GetProperty("activityName").GetString());
        Assert.Equal(clock.GetUtcNow(), Collection(updated, "Activity").GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(Collection(saved, "AthleteProfile").ToString(), Collection(updated, "AthleteProfile").ToString());

        using var unnamed = await client.PostAsJsonAsync("/ingest", new
        {
            version = 2,
            activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-22" },
            activities = new[] { new { activityId = 24444892080L, startTimeUtc = "2026-09-21T14:34:01Z",
                typeKey = "strength_training", durationSeconds = 3100.0, distanceMeters = 0.0,
                totalSets = 14, activeSets = 14, totalReps = 82 } }
        });
        Assert.Equal(HttpStatusCode.OK, unnamed.StatusCode);
        var preserved = await CallTool(client, "get_sync_status");
        Assert.Equal(3100.0, preserved.GetProperty("latestActivity").GetProperty("durationSeconds").GetDouble());
        Assert.Equal("Sunday strength", preserved.GetProperty("latestActivity").GetProperty("activityName").GetString());
    }

    [Fact]
    public async Task Invalid_version_two_activity_rejects_the_whole_batch()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        using var response = await client.PostAsJsonAsync("/ingest", new
        {
            version = 2,
            athleteProfiles = new[] { new { bodyWeightKg = 82.5m } },
            activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-22" },
            activities = new[] { new { activityId = 24444892080L, startTimeUtc = "2026-09-21T14:34:01Z",
                typeKey = "strength_training", durationSeconds = -1m, distanceMeters = 0m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
        Assert.Equal(JsonValueKind.Null, (await CallTool(client, "get_athlete_profile")).GetProperty("profile").ValueKind);
    }

    [Fact]
    public async Task Empty_activity_range_is_distinct_from_never_synced_and_replay_preserves_its_time()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var batch = new
        {
            version = 2,
            activityRange = new { fromDate = "2030-04-09", toDate = "2030-04-10" },
            activities = Array.Empty<object>()
        };
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var first = Collection(await CallTool(client, "get_sync_status"), "Activity");
        Assert.Equal(0, first.GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), first.GetProperty("lastSyncedAt").GetDateTimeOffset());

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replay = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.ToString(), Collection(await CallTool(client, "get_sync_status"), "Activity").ToString());
    }

    [Fact]
    public async Task Reposting_version_two_does_not_append_a_profile_constraint_again()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var batch = new
        {
            version = 2,
            athleteProfiles = new[] { new { constraint = new
            {
                description = "Travel week", validFrom = "2030-04-10T00:00:00Z", validUntil = "2030-04-17T00:00:00Z"
            } } },
            activityRange = new { fromDate = "2030-04-10", toDate = "2030-04-10" },
            activities = Array.Empty<object>()
        };
        using var first = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var before = await CallTool(client, "get_athlete_profile");
        var status = await CallTool(client, "get_sync_status");
        Assert.Equal(new[] { "Travel week" }, ConstraintDescriptions(before));

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replay = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(new[] { "Travel week" }, ConstraintDescriptions(await CallTool(client, "get_athlete_profile")));
        Assert.Equal(status.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Theory]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-22\",\"toDate\":\"2026-09-21\"},\"activities\":[]}")]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-22\"},\"activities\":[{\"activityId\":1,\"startTimeUtc\":\"2026-09-21T14:34:01Z\",\"typeKey\":\"running\"}]}")]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-22\"},\"activities\":[null]}")]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-22\"},\"activities\":[],\"userId\":\"another-athlete\"}")]
    public async Task Invalid_version_two_input_keeps_all_collections_unchanged(string batch)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task Strength_exercise_sets_persist_in_kilograms_and_report_strength_detail()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var exerciseSets = FixtureExerciseSets(24444892080);
        var batch = StrengthBatch(exerciseSets);
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var saved = await CallTool(client, "get_sync_status");
        Assert.Equal(28, Collection(saved, "ExerciseSet").GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), Collection(saved, "ExerciseSet").GetProperty("lastSyncedAt").GetDateTimeOffset());
        var detail = saved.GetProperty("latestActivityStrengthDetail");
        var rows = detail.GetProperty("exerciseSets");
        Assert.Equal(28, rows.GetArrayLength());
        var first = rows[0];
        Assert.Equal("BANDED_EXERCISES", first.GetProperty("exercise").GetProperty("category").GetString());
        Assert.Equal("GLUTE_BRIDGE", first.GetProperty("exercise").GetProperty("name").GetString());
        Assert.Equal(("ACTIVE", 8, 0m, false), (first.GetProperty("setType").GetString(),
            first.GetProperty("repetitions").GetInt32(), first.GetProperty("weightKg").GetDecimal(),
            first.GetProperty("bodyweight").GetBoolean()));
        Assert.True(first.GetProperty("working").GetBoolean());
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 14, 34, 1, TimeSpan.Zero),
            first.GetProperty("startTimeUtc").GetDateTimeOffset());
        Assert.Equal(0, first.GetProperty("wktStepIndex").GetInt32());
        Assert.Equal(3, first.GetProperty("candidateCount").GetInt32());
        Assert.Equal(99.609375, first.GetProperty("topProbability").GetDouble());
        var bench = rows[5];
        Assert.Equal("BENCH_PRESS", bench.GetProperty("exercise").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, bench.GetProperty("exercise").GetProperty("name").ValueKind);
        Assert.Equal(75m, bench.GetProperty("weightKg").GetDecimal());
        var rest = rows[1];
        Assert.Equal("REST", rest.GetProperty("setType").GetString());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("repetitions").ValueKind);
        Assert.False(rest.GetProperty("cancelled").GetBoolean());
        var cancelled = rows[6];
        Assert.True(cancelled.GetProperty("cancelled").GetBoolean());
        Assert.False(cancelled.GetProperty("working").GetBoolean());
        Assert.Equal(0, cancelled.GetProperty("repetitions").GetInt32());
        Assert.Equal(85m, cancelled.GetProperty("weightKg").GetDecimal());
        Assert.Equal(12, detail.GetProperty("workingExerciseSets").GetInt32());
        Assert.Equal(2, detail.GetProperty("cancelledExerciseSets").GetInt32());
        Assert.Equal(3482m, detail.GetProperty("volumeKg").GetDecimal());
        Assert.Equal(82d / 12, detail.GetProperty("averageRepetitions").GetDouble());

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replayed = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(saved.ToString(), (await CallTool(client, "get_sync_status")).ToString());

        clock.Set(clock.GetUtcNow().AddDays(1));
        var corrected = exerciseSets.Take(27)
            .Select((exerciseSet, index) => index != 5 ? exerciseSet : exerciseSet with { WeightKg = 77.5m }).ToArray();
        using var reposted = await client.PostAsJsonAsync("/ingest", StrengthBatch(corrected));
        Assert.Equal(HttpStatusCode.OK, reposted.StatusCode);
        var updated = await CallTool(client, "get_sync_status");
        Assert.Equal(27, Collection(updated, "ExerciseSet").GetProperty("count").GetInt32());
        Assert.Equal(77.5m, updated.GetProperty("latestActivityStrengthDetail")
            .GetProperty("exerciseSets")[5].GetProperty("weightKg").GetDecimal());

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var unenriched = await client.PostAsJsonAsync("/ingest", StrengthBatch(null));
        Assert.Equal(HttpStatusCode.OK, unenriched.StatusCode);
        var preserved = await CallTool(client, "get_sync_status");
        Assert.Equal(27, Collection(preserved, "ExerciseSet").GetProperty("count").GetInt32());
        Assert.Equal(77.5m, preserved.GetProperty("latestActivityStrengthDetail")
            .GetProperty("exerciseSets")[5].GetProperty("weightKg").GetDecimal());
    }

    [Fact]
    public async Task Bodyweight_working_set_is_a_stated_load_distinct_from_no_load_at_all()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        using var posted = await client.PostAsJsonAsync("/ingest", new
        {
            version = 2,
            activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-21" },
            activities = new[] { new
            {
                activityId = 991L, startTimeUtc = "2026-09-21T10:00:00Z", typeKey = "strength_training",
                durationSeconds = 600.0,
                exerciseSets = new[]
                {
                    new ExerciseSetInput("ACTIVE", Repetitions: 5, Bodyweight: true),
                    new ExerciseSetInput("ACTIVE", Repetitions: 8, WeightKg: 0m)
                }
            } }
        });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var detail = (await CallTool(client, "get_sync_status")).GetProperty("latestActivityStrengthDetail");
        var stated = detail.GetProperty("exerciseSets")[0];
        Assert.True(stated.GetProperty("bodyweight").GetBoolean());
        Assert.Equal(JsonValueKind.Null, stated.GetProperty("weightKg").ValueKind);
        Assert.True(stated.GetProperty("working").GetBoolean());
        var unloaded = detail.GetProperty("exerciseSets")[1];
        Assert.False(unloaded.GetProperty("bodyweight").GetBoolean());
        Assert.Equal(0m, unloaded.GetProperty("weightKg").GetDecimal());
        Assert.Equal(2, detail.GetProperty("workingExerciseSets").GetInt32());
        Assert.Equal(0m, detail.GetProperty("volumeKg").GetDecimal());
        Assert.Equal(6.5, detail.GetProperty("averageRepetitions").GetDouble());
    }

    [Theory]
    [InlineData("{\"setType\":\"ACTIVE\",\"repetitions\":5,\"weightKg\":75.0,\"bodyweight\":true}")]
    [InlineData("{\"setType\":\"ACTIVE\",\"repetitions\":-1}")]
    [InlineData("{\"setType\":\"ACTIVE\",\"repetitions\":5,\"weightKg\":75.00001}")]
    [InlineData("{\"setType\":\"  \",\"repetitions\":5}")]
    [InlineData("{\"repetitions\":5}")]
    [InlineData("{\"setType\":\"ACTIVE\",\"exercise\":{\"category\":\"  \"}}")]
    [InlineData("null")]
    public async Task Invalid_exercise_set_rejects_the_whole_batch(string setJson)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        var batch = "{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},"
            + "\"activities\":[{\"activityId\":992,\"startTimeUtc\":\"2026-09-21T10:00:00Z\","
            + "\"typeKey\":\"strength_training\",\"durationSeconds\":600.0,\"exerciseSets\":[" + setJson + "]}]}";
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task Cardio_time_in_zones_persists_against_the_default_sport_profile()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        Assert.Equal(0, Collection(before, "SportProfile").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, Collection(before, "SportProfile").GetProperty("lastSyncedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("latestActivityCardioDetail").ValueKind);

        var batch = CardioBatch(FixtureSportProfiles(), FixtureCardioActivity(24174672446), FixtureCardioActivity(24399329227));
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var saved = await CallTool(client, "get_sync_status");
        Assert.Equal(1, Collection(saved, "SportProfile").GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), Collection(saved, "SportProfile").GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(24399329227L, saved.GetProperty("latestActivity").GetProperty("activityId").GetInt64());
        var detail = saved.GetProperty("latestActivityCardioDetail");
        var profile = detail.GetProperty("sportProfile");
        Assert.Equal("DEFAULT", profile.GetProperty("name").GetString());
        Assert.Equal("HR_RESERVE", profile.GetProperty("trainingMethod").GetString());
        Assert.Equal((51, 180), (profile.GetProperty("restingHeartRateBpm").GetInt32(), profile.GetProperty("maxHeartRateBpm").GetInt32()));
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("lactateThresholdHeartRateBpm").ValueKind);
        Assert.Equal(new[] { (1, 116), (2, 128), (3, 141), (4, 154), (5, 167) }, profile.GetProperty("heartRateZones")
            .EnumerateArray().Select(zone => (zone.GetProperty("zoneNumber").GetInt32(), zone.GetProperty("lowBoundaryBpm").GetInt32())));
        Assert.Equal(clock.GetUtcNow(), detail.GetProperty("sportProfileObservedAt").GetDateTimeOffset());
        Assert.Equal(new[] { (1, 591.39), (2, 378.007), (3, 759.999), (4, 306.997), (5, 178.384) }, detail.GetProperty("timeInHeartRateZones")
            .EnumerateArray().Select(zone => (zone.GetProperty("zoneNumber").GetInt32(), zone.GetProperty("seconds").GetDouble())));

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replayed = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(saved.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task A_later_boundary_change_does_not_alter_a_stored_activity()
    {
        var observed = new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(observed);
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var run = FixtureCardioActivity(24399329227);
        using var first = await client.PostAsJsonAsync("/ingest", CardioBatch(FixtureSportProfiles(), run));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var changedAt = observed.AddDays(1);
        clock.Set(changedAt);
        var changed = new[] { FixtureSportProfiles()[0] with { HeartRateZones = [new(1, 120), new(2, 132), new(3, 145), new(4, 158), new(5, 170)] } };
        using var resynced = await client.PostAsJsonAsync("/ingest", CardioBatch(changed, run));
        Assert.Equal(HttpStatusCode.OK, resynced.StatusCode);
        var kept = await CallTool(client, "get_sync_status");
        var keptDetail = kept.GetProperty("latestActivityCardioDetail");
        Assert.Equal(116, keptDetail.GetProperty("sportProfile").GetProperty("heartRateZones")[0].GetProperty("lowBoundaryBpm").GetInt32());
        Assert.Equal(observed, keptDetail.GetProperty("sportProfileObservedAt").GetDateTimeOffset());
        Assert.Equal(1, Collection(kept, "SportProfile").GetProperty("count").GetInt32());
        Assert.Equal(changedAt, Collection(kept, "SportProfile").GetProperty("lastSyncedAt").GetDateTimeOffset());

        clock.Set(changedAt.AddDays(1));
        var laterRun = run with { ActivityId = 24499329227, StartTimeUtc = "2026-09-24T17:19:25Z" };
        using var later = await client.PostAsJsonAsync("/ingest", CardioBatch(changed, laterRun));
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var laterDetail = (await CallTool(client, "get_sync_status")).GetProperty("latestActivityCardioDetail");
        Assert.Equal(120, laterDetail.GetProperty("sportProfile").GetProperty("heartRateZones")[0].GetProperty("lowBoundaryBpm").GetInt32());
        Assert.Equal(changedAt, laterDetail.GetProperty("sportProfileObservedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Clearing_time_in_zones_keeps_the_pin_and_a_changed_type_is_measured_against_its_own_sport_profile()
    {
        var observed = new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableClock(observed);
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var cycling = new SportProfileInput("CYCLING", "HR_RESERVE", 51, 175, null, [new(1, 110), new(2, 124)]);
        var run = FixtureCardioActivity(24399329227);
        using var first = await client.PostAsJsonAsync("/ingest", CardioBatch([FixtureSportProfiles()[0], cycling], run));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        clock.Set(observed.AddDays(1));
        using var cleared = await client.PostAsJsonAsync("/ingest", CardioBatch([FixtureSportProfiles()[0], cycling],
            run with { TimeInHeartRateZones = [] }));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await CallTool(client, "get_sync_status")).GetProperty("latestActivityCardioDetail").ValueKind);

        clock.Set(observed.AddDays(2));
        var changed = FixtureSportProfiles()[0] with { HeartRateZones = [new(1, 120), new(2, 132), new(3, 145), new(4, 158), new(5, 170)] };
        using var resent = await client.PostAsJsonAsync("/ingest", CardioBatch([changed, cycling], run));
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var kept = (await CallTool(client, "get_sync_status")).GetProperty("latestActivityCardioDetail");
        Assert.Equal(116, kept.GetProperty("sportProfile").GetProperty("heartRateZones")[0].GetProperty("lowBoundaryBpm").GetInt32());

        using var retyped = await client.PostAsJsonAsync("/ingest", CardioBatch([changed, cycling], run with { TypeKey = "indoor_cycling" }));
        Assert.Equal(HttpStatusCode.OK, retyped.StatusCode);
        var ride = (await CallTool(client, "get_sync_status")).GetProperty("latestActivityCardioDetail");
        Assert.Equal("CYCLING", ride.GetProperty("sportProfile").GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_export_backfill_after_an_api_sync_fills_only_the_gaps_and_leaves_api_values_and_freshness_unchanged()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        var run = FixtureCardioActivity(24313465171);
        using var synced = await client.PostAsJsonAsync("/ingest", new
        {
            version = 3,
            activityRange = new { fromDate = "2026-08-30", toDate = "2026-09-16" },
            sportProfiles = FixtureSportProfiles(),
            activities = new object[]
            {
                FixtureCardioActivity(24174672446),
                FixtureCardioActivity(24272601315) with { TimeInHeartRateZones = [] },
                // Renamed in Garmin after the export was taken, so only the API knows the name.
                new { run.ActivityId, activityName = "Tempo by the river", run.StartTimeUtc, run.TypeKey,
                    run.DurationSeconds, run.TimeInHeartRateZones }
            },
            sleepNights = new[] { "2026-09-15", "2026-09-16" }.Select(date => new
            {
                calendarDate = date, startTimeUtc = $"{date}T00:30:00Z", endTimeUtc = $"{date}T06:30:00Z", sleepMinutes = 340
            }),
            // Days the watch recorded no BodyBattery; the export's records of them hold one.
            dailySummaries = new[] { "2026-09-15", "2026-09-16" }.Select(date => new
            {
                calendarDate = date, stressLevel = new { average = 30, maximum = 91 }
            })
        });
        Assert.Equal(HttpStatusCode.OK, synced.StatusCode);
        var before = await CallTool(client, "get_sync_status");

        clock.Set(clock.GetUtcNow().AddDays(1));
        await PostExportBatches(client);

        var after = await CallTool(client, "get_sync_status");
        // The two 2024 Activities and the two June days are the gaps.
        foreach (var (name, filled) in new[] { ("Activity", 2), ("SleepNight", 2), ("DailySummary", 2) })
            Assert.Equal(Collection(before, name).GetProperty("count").GetInt32() + filled,
                Collection(after, name).GetProperty("count").GetInt32());
        foreach (var collection in before.GetProperty("collections").EnumerateArray())
            Assert.Equal(collection.GetProperty("lastSyncedAt").ToString(),
                Collection(after, collection.GetProperty("name").GetString()!).GetProperty("lastSyncedAt").ToString());
        foreach (var property in new[] { "latestActivity", "latestActivityCardioDetail", "latestWellnessDay", "lastSyncedAt" })
            Assert.Equal(before.GetProperty(property).ToString(), after.GetProperty(property).ToString());
        Assert.Equal("Tempo by the river", after.GetProperty("latestActivity").GetProperty("activityName").GetString());
        Assert.DoesNotContain("BodyBattery", after.GetProperty("latestWellnessDay").GetProperty("holds")
            .EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task The_export_backfill_into_an_empty_store_fills_its_history_without_reporting_a_sync()
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);

        await PostExportBatches(client);

        var filled = await CallTool(client, "get_sync_status");
        foreach (var (name, count) in new[] { ("Activity", 5), ("SleepNight", 4), ("DailySummary", 4) })
        {
            Assert.Equal(count, Collection(filled, name).GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, Collection(filled, name).GetProperty("lastSyncedAt").ValueKind);
        }
        Assert.Equal(JsonValueKind.Null, filled.GetProperty("lastSyncedAt").ValueKind);
        // The latest day reached is a sync's to report; the export reaches none.
        Assert.Equal(JsonValueKind.Null, filled.GetProperty("latestWellnessDay").ValueKind);
        var latest = filled.GetProperty("latestActivity");
        Assert.Equal((24313465171L, "running", 2490.14990234375), (latest.GetProperty("activityId").GetInt64(),
            latest.GetProperty("activityName").GetString(), latest.GetProperty("distanceMeters").GetDouble()));

        await PostExportBatches(client);
        Assert.Equal(filled.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    // Each would move a sync time or replace synced figures, which a backfill never does.
    [Theory]
    [InlineData("{\"version\":1,\"backfill\":true,\"athleteProfiles\":[{\"bodyWeightKg\":65}]}")]
    [InlineData("{\"version\":3,\"backfill\":true,\"athleteProfiles\":[{\"bodyWeightKg\":65}],"
        + "\"activityRange\":{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":2,\"backfill\":true,\"sportProfiles\":[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":100}]}],"
        + "\"activityRange\":{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\"},\"activities\":[]}")]
    [InlineData("{\"version\":4,\"backfill\":true,\"activityRange\":{\"fromDate\":\"2026-09-01\",\"toDate\":\"2026-09-30\"},"
        + "\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[],\"trainingLoads\":[],\"vo2maxEstimates\":[]}")]
    public async Task A_backfill_carrying_anything_but_activities_and_daily_wellness_is_rejected(string batch)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");

        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Theory]
    [InlineData("running", "RUNNING")]
    [InlineData("trail_running", "RUNNING")]
    [InlineData("indoor_cycling", "CYCLING")]
    [InlineData("road_biking", "CYCLING")]
    [InlineData("lap_swimming", "DEFAULT")]
    [InlineData("underwater_hockey", "DEFAULT")]
    public async Task An_activity_resolves_to_its_sport_override_and_otherwise_to_the_default(string typeKey, string expected)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var profiles = new[]
        {
            FixtureSportProfiles()[0],
            new SportProfileInput("RUNNING", "HR_RESERVE", 51, 185, null, [new(1, 120), new(2, 133)]),
            new SportProfileInput("CYCLING", "HR_RESERVE", 51, 175, null, [new(1, 110), new(2, 124)])
        };
        using var posted = await client.PostAsJsonAsync("/ingest",
            CardioBatch(profiles, FixtureCardioActivity(24399329227) with { TypeKey = typeKey }));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var detail = (await CallTool(client, "get_sync_status")).GetProperty("latestActivityCardioDetail");
        Assert.Equal(expected, detail.GetProperty("sportProfile").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("", "[{\"zoneNumber\":1,\"seconds\":60}]")]
    [InlineData("[{\"name\":\"RUNNING\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "[{\"zoneNumber\":1,\"seconds\":60}]")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]},{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "null")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":130},{\"zoneNumber\":2,\"lowBoundaryBpm\":120}]}]", "null")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[]}]", "null")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":0,\"lowBoundaryBpm\":120}]}]", "null")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "[{\"zoneNumber\":1,\"seconds\":-1}]")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "[{\"zoneNumber\":1,\"seconds\":60},{\"zoneNumber\":1,\"seconds\":60}]")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "[{\"zoneNumber\":1}]")]
    [InlineData("[{\"name\":\"DEFAULT\",\"heartRateZones\":[{\"zoneNumber\":1,\"lowBoundaryBpm\":120}]}]", "[null]")]
    public async Task Invalid_time_in_zones_or_sport_profiles_reject_the_whole_batch(string sportProfiles, string timeInZones)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        var batch = "{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-17\",\"toDate\":\"2026-09-17\"},"
            + (sportProfiles == "" ? "" : "\"sportProfiles\":" + sportProfiles + ",")
            + "\"activities\":[{\"activityId\":993,\"startTimeUtc\":\"2026-09-17T17:19:25Z\","
            + "\"typeKey\":\"running\",\"durationSeconds\":2400.0,\"timeInHeartRateZones\":" + timeInZones + "}]}";
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task Version_three_stores_a_days_sleep_night_and_daily_summary_and_reports_only_counts_freshness_and_what_it_holds()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var activitiesOnly = await client.PostAsJsonAsync("/ingest", new
        {
            version = 2, activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-21" }, activities = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, activitiesOnly.StatusCode);
        var before = await CallTool(client, "get_sync_status");
        foreach (var name in new[] { "SleepNight", "DailySummary" })
        {
            Assert.Equal(0, Collection(before, name).GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, Collection(before, name).GetProperty("lastSyncedAt").ValueKind);
        }
        Assert.Equal(JsonValueKind.Null, before.GetProperty("latestWellnessDay").ValueKind);

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var earlier = await client.PostAsJsonAsync("/ingest",
            WellnessBatch("2026-09-20", [FixtureSleepNight("2026-09-20")], [FixtureDailySummary("2026-09-20")]));
        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
        clock.Set(clock.GetUtcNow().AddHours(1));
        var batch = WellnessBatch("2026-09-21", [FixtureSleepNight("2026-09-21")], [FixtureDailySummary("2026-09-21")]);
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var saved = await CallTool(client, "get_sync_status");
        foreach (var name in new[] { "SleepNight", "DailySummary" })
        {
            Assert.Equal(2, Collection(saved, name).GetProperty("count").GetInt32());
            Assert.Equal(clock.GetUtcNow(), Collection(saved, name).GetProperty("lastSyncedAt").GetDateTimeOffset());
        }
        // A version 3 batch is also an Activity sync that found none.
        Assert.Equal(clock.GetUtcNow(), Collection(saved, "Activity").GetProperty("lastSyncedAt").GetDateTimeOffset());
        var latest = saved.GetProperty("latestWellnessDay");
        Assert.Equal(new[] { "calendarDate", "holds" }, latest.EnumerateObject().Select(property => property.Name));
        Assert.Equal("2026-09-21", latest.GetProperty("calendarDate").GetString());
        Assert.Equal(new[] { "SleepNight", "DailySummary", "BodyBattery", "StressLevel" },
            latest.GetProperty("holds").EnumerateArray().Select(item => item.GetString()));
        // No wellness value is returned by any tool: what was stored shows only as counts and holds.
        Assert.Equal(new[] { "collections", "latestActivity", "latestActivityStrengthDetail", "latestActivityCardioDetail",
            "latestWellnessDay", "lastSyncedAt" }, saved.EnumerateObject().Select(property => property.Name));

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replayed = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(saved.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task A_day_garmin_holds_nothing_for_is_reported_as_the_latest_day_reached_holding_nothing()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var stored = await client.PostAsJsonAsync("/ingest",
            WellnessBatch("2026-09-21", [FixtureSleepNight("2026-09-21")], [FixtureDailySummary("2026-09-21")]));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var empty = await client.PostAsJsonAsync("/ingest", WellnessBatch("2026-09-22", [], []));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);

        var reached = await CallTool(client, "get_sync_status");
        foreach (var name in new[] { "SleepNight", "DailySummary" })
        {
            Assert.Equal(1, Collection(reached, name).GetProperty("count").GetInt32());
            Assert.Equal(clock.GetUtcNow(), Collection(reached, name).GetProperty("lastSyncedAt").GetDateTimeOffset());
        }
        Assert.Equal("2026-09-22", reached.GetProperty("latestWellnessDay").GetProperty("calendarDate").GetString());
        Assert.Empty(reached.GetProperty("latestWellnessDay").GetProperty("holds").EnumerateArray());

        // A catch-up over an older day refreshes freshness but is not the latest day reached.
        clock.Set(clock.GetUtcNow().AddHours(1));
        using var older = await client.PostAsJsonAsync("/ingest",
            WellnessBatch("2026-09-20", [FixtureSleepNight("2026-09-20")], []));
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        var caughtUp = await CallTool(client, "get_sync_status");
        Assert.Equal(2, Collection(caughtUp, "SleepNight").GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), Collection(caughtUp, "DailySummary").GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.Equal(reached.GetProperty("latestWellnessDay").ToString(), caughtUp.GetProperty("latestWellnessDay").ToString());
    }

    [Fact]
    public async Task Resending_a_day_replaces_its_figures_and_a_day_sent_empty_keeps_what_is_stored()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var first = await client.PostAsJsonAsync("/ingest",
            WellnessBatch("2026-09-21", [FixtureSleepNight("2026-09-21")], [FixtureDailySummary("2026-09-21")]));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var resent = await client.PostAsJsonAsync("/ingest", WellnessBatch("2026-09-21", [],
            [new { calendarDate = "2026-09-21", stressLevel = new { average = 30, maximum = 95 } }]));
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);

        var status = await CallTool(client, "get_sync_status");
        Assert.Equal(1, Collection(status, "SleepNight").GetProperty("count").GetInt32());
        Assert.Equal(1, Collection(status, "DailySummary").GetProperty("count").GetInt32());
        Assert.Equal(new[] { "SleepNight", "DailySummary", "StressLevel" },
            status.GetProperty("latestWellnessDay").GetProperty("holds").EnumerateArray().Select(item => item.GetString()));
    }

    [Theory]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[]}")]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[{\"calendarDate\":\"2026-09-22\",\"startTimeUtc\":\"2026-09-21T20:54:00Z\",\"endTimeUtc\":\"2026-09-22T04:08:00Z\"}],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[{\"calendarDate\":\"2026-09-21\",\"startTimeUtc\":\"2026-09-21T04:08:00Z\",\"endTimeUtc\":\"2026-09-20T20:54:00Z\"}],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[{\"calendarDate\":\"2026-09-21\",\"startTimeUtc\":\"2026-09-20T20:54:00Z\",\"endTimeUtc\":\"2026-09-21T04:08:00Z\",\"deepSleepMinutes\":-1}],\"dailySummaries\":[]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\"},{\"calendarDate\":\"2026-09-21\"}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\",\"bodyBattery\":{\"highest\":101}}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\",\"stressLevel\":{\"average\":101}}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\",\"garminResponse\":{\"stressValuesArray\":[[1789941600000,70]]}}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\",\"garminResponse\":{\"rule\":{\"levels\":[1]}}}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[{\"calendarDate\":\"2026-09-21\",\"garminResponse\":\"not an object\"}]}")]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[null],\"dailySummaries\":[]}")]
    public async Task Invalid_version_three_input_keeps_all_collections_unchanged(string batch)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task Version_four_stores_a_days_training_load_and_vo2max_estimates_and_reports_only_counts_freshness_and_what_it_holds()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var wellnessOnly = await client.PostAsJsonAsync("/ingest",
            WellnessBatch("2026-09-19", [FixtureSleepNight("2026-09-19")], [FixtureDailySummary("2026-09-19")]));
        Assert.Equal(HttpStatusCode.OK, wellnessOnly.StatusCode);
        var before = await CallTool(client, "get_sync_status");
        foreach (var name in new[] { "TrainingLoad", "VO2max" })
        {
            Assert.Equal(0, Collection(before, name).GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, Collection(before, name).GetProperty("lastSyncedAt").ValueKind);
        }

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var earlier = await client.PostAsJsonAsync("/ingest", DailyFormBatch("2026-09-20"));
        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
        clock.Set(clock.GetUtcNow().AddHours(1));
        var batch = DailyFormBatch("2026-09-21");
        using var posted = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var saved = await CallTool(client, "get_sync_status");
        Assert.Equal(2, Collection(saved, "TrainingLoad").GetProperty("count").GetInt32());
        // Both days re-read the same generic and cycling estimates, kept under the dates Garmin computed them.
        Assert.Equal(2, Collection(saved, "VO2max").GetProperty("count").GetInt32());
        foreach (var name in new[] { "TrainingLoad", "VO2max", "SleepNight", "DailySummary", "Activity" })
            Assert.Equal(clock.GetUtcNow(), Collection(saved, name).GetProperty("lastSyncedAt").GetDateTimeOffset());
        var latest = saved.GetProperty("latestWellnessDay");
        Assert.Equal("2026-09-21", latest.GetProperty("calendarDate").GetString());
        Assert.Equal(new[] { "SleepNight", "DailySummary", "BodyBattery", "StressLevel", "TrainingLoad" },
            latest.GetProperty("holds").EnumerateArray().Select(item => item.GetString()));
        // No daily form value is returned by any tool: what was stored shows only as counts and holds.
        Assert.Equal(new[] { "collections", "latestActivity", "latestActivityStrengthDetail", "latestActivityCardioDetail",
            "latestWellnessDay", "lastSyncedAt" }, saved.EnumerateObject().Select(property => property.Name));

        clock.Set(clock.GetUtcNow().AddDays(1));
        using var replayed = await client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(saved.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Fact]
    public async Task A_day_without_training_status_is_told_apart_from_a_zero_load_and_a_new_estimate_adds_one()
    {
        var clock = new MutableClock(new DateTimeOffset(2030, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await using var host = new CoachHost(database) { Clock = clock };
        using var client = AthleteClient(host);
        using var stored = await client.PostAsJsonAsync("/ingest", DailyFormBatch("2026-09-21"));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var nothing = await client.PostAsJsonAsync("/ingest", DailyFormBatch("2026-09-22", [], []));
        Assert.Equal(HttpStatusCode.OK, nothing.StatusCode);
        var absent = await CallTool(client, "get_sync_status");
        Assert.Equal(1, Collection(absent, "TrainingLoad").GetProperty("count").GetInt32());
        Assert.Equal(2, Collection(absent, "VO2max").GetProperty("count").GetInt32());
        Assert.Equal(clock.GetUtcNow(), Collection(absent, "TrainingLoad").GetProperty("lastSyncedAt").GetDateTimeOffset());
        Assert.DoesNotContain("TrainingLoad", absent.GetProperty("latestWellnessDay").GetProperty("holds")
            .EnumerateArray().Select(item => item.GetString()));

        // A week of rest is a zero load, stored, even on a day carrying no VO2max.
        clock.Set(clock.GetUtcNow().AddHours(1));
        using var rested = await client.PostAsJsonAsync("/ingest", DailyFormBatch("2026-09-23",
            [new { calendarDate = "2026-09-23", weeklyTrainingLoad = 0, loadTunnelMin = 67, loadTunnelMax = 218 }], []));
        Assert.Equal(HttpStatusCode.OK, rested.StatusCode);
        var zero = await CallTool(client, "get_sync_status");
        Assert.Equal(2, Collection(zero, "TrainingLoad").GetProperty("count").GetInt32());
        Assert.Equal(2, Collection(zero, "VO2max").GetProperty("count").GetInt32());
        Assert.Contains("TrainingLoad", zero.GetProperty("latestWellnessDay").GetProperty("holds")
            .EnumerateArray().Select(item => item.GetString()));

        clock.Set(clock.GetUtcNow().AddHours(1));
        using var recomputed = await client.PostAsJsonAsync("/ingest", DailyFormBatch("2026-09-24", [],
            [new { sport = "generic", calendarDate = "2026-09-24", mlPerKgMin = 39.6 }]));
        Assert.Equal(HttpStatusCode.OK, recomputed.StatusCode);
        Assert.Equal(3, Collection(await CallTool(client, "get_sync_status"), "VO2max").GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("\"trainingLoads\":[]")]
    [InlineData("\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[null],\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[{\"calendarDate\":\"2026-09-22\",\"weeklyTrainingLoad\":140}],\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[{\"calendarDate\":\"2026-09-21\"},{\"calendarDate\":\"2026-09-21\"}],\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[{\"calendarDate\":\"2026-09-21\",\"weeklyTrainingLoad\":-1}],\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[{\"calendarDate\":\"2026-09-21\",\"garminResponse\":{\"recordedDevices\":[]}}],\"vo2maxEstimates\":[]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[null]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[{\"sport\":\"generic\",\"calendarDate\":\"2026-09-22\",\"mlPerKgMin\":39.3}]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[{\"sport\":\"running\",\"calendarDate\":\"2026-09-02\",\"mlPerKgMin\":39.3}]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[{\"sport\":\"generic\",\"calendarDate\":\"2026-09-02\",\"mlPerKgMin\":0}]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[{\"sport\":\"generic\",\"calendarDate\":\"2026-09-02\"}]")]
    [InlineData("\"trainingLoads\":[],\"vo2maxEstimates\":[{\"sport\":\"generic\",\"calendarDate\":\"2026-09-02\",\"mlPerKgMin\":39.3},{\"sport\":\"generic\",\"calendarDate\":\"2026-09-02\",\"mlPerKgMin\":39.0}]")]
    public async Task Invalid_version_four_input_keeps_all_collections_unchanged(string dailyForm)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        var batch = "{\"version\":4,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},"
            + "\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[]," + dailyForm + "}";
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    [Theory]
    [InlineData("{\"version\":3,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"sleepNights\":[],\"dailySummaries\":[],\"trainingLoads\":[],\"vo2maxEstimates\":[]}")]
    [InlineData("{\"version\":2,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"vo2maxEstimates\":[]}")]
    [InlineData("{\"version\":1,\"athleteProfiles\":[{\"bodyWeightKg\":65}],\"trainingLoads\":[]}")]
    [InlineData("{\"version\":4,\"activityRange\":{\"fromDate\":\"2026-09-21\",\"toDate\":\"2026-09-21\"},\"activities\":[],\"trainingLoads\":[],\"vo2maxEstimates\":[]}")]
    public async Task Daily_form_travels_only_in_version_four_beside_the_days_wellness(string batch)
    {
        await using var host = new CoachHost(database);
        using var client = AthleteClient(host);
        var before = await CallTool(client, "get_sync_status");
        using var content = new StringContent(batch, System.Text.Encoding.UTF8, "application/json");
        using var rejected = await client.PostAsync("/ingest", content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.ToString(), (await CallTool(client, "get_sync_status")).ToString());
    }

    private static object WellnessBatch(string date, object[] sleepNights, object[] dailySummaries) => new
    {
        version = 3,
        activityRange = new { fromDate = date, toDate = date },
        activities = Array.Empty<object>(),
        sleepNights,
        dailySummaries
    };

    private static string Fixture(string name) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, $"../../../../../fixtures/garmin/{name}")));

    // What the export importer posts for its fixture archive, asserted on the Python side to be exactly that.
    private static async Task PostExportBatches(HttpClient client)
    {
        using var batches = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../tests/python/fixtures/garmin-export/ingest-batches.json"))));
        foreach (var batch in batches.RootElement.EnumerateArray())
        {
            using var content = new StringContent(batch.GetRawText(), System.Text.Encoding.UTF8, "application/json");
            using var posted = await client.PostAsync("/ingest", content);
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        }
    }

    private static int? Int(JsonElement entry, string name) =>
        entry.GetProperty(name).ValueKind == JsonValueKind.Null ? null : entry.GetProperty(name).GetInt32();

    // The archive keeps Garmin's scalars and drops every array-valued member, as the sync does.
    private static JsonNode? WithoutArrays(JsonNode? node) => node is JsonObject members
        ? new JsonObject(members.Where(member => member.Value is not JsonArray)
            .Select(member => KeyValuePair.Create(member.Key, WithoutArrays(member.Value))))
        : node?.DeepClone();

    private static int? Minutes(JsonElement entry, string name) =>
        Int(entry, name) is { } seconds ? (int)Math.Round(seconds / 60.0, MidpointRounding.ToEven) : null;

    // Mapped as the sync maps them: Garmin's GMT epoch milliseconds become UTC timestamps
    // and its seconds become whole minutes.
    private static object FixtureSleepNight(string date)
    {
        var text = Fixture($"wellness-service--dailySleepData__date-{date}.json");
        using var fixture = JsonDocument.Parse(text);
        var sleep = fixture.RootElement.GetProperty("dailySleepDTO");
        string Utc(string name) => DateTimeOffset.FromUnixTimeMilliseconds(sleep.GetProperty(name).GetInt64())
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return new
        {
            calendarDate = date,
            startTimeUtc = Utc("sleepStartTimestampGMT"),
            endTimeUtc = Utc("sleepEndTimestampGMT"),
            sleepMinutes = Minutes(sleep, "sleepTimeSeconds"),
            deepSleepMinutes = Minutes(sleep, "deepSleepSeconds"),
            lightSleepMinutes = Minutes(sleep, "lightSleepSeconds"),
            remSleepMinutes = Minutes(sleep, "remSleepSeconds"),
            awakeSleepMinutes = Minutes(sleep, "awakeSleepSeconds"),
            unmeasurableSleepMinutes = Minutes(sleep, "unmeasurableSleepSeconds"),
            garminResponse = WithoutArrays(JsonNode.Parse(text))
        };
    }

    private static object FixtureDailySummary(string date)
    {
        var text = Fixture($"usersummary-service--daily__date-{date}.json");
        using var fixture = JsonDocument.Parse(text);
        var summary = fixture.RootElement;
        return new
        {
            calendarDate = date,
            bodyBattery = new
            {
                charged = Int(summary, "bodyBatteryChargedValue"),
                drained = Int(summary, "bodyBatteryDrainedValue"),
                highest = Int(summary, "bodyBatteryHighestValue"),
                lowest = Int(summary, "bodyBatteryLowestValue")
            },
            stressLevel = new
            {
                average = Int(summary, "averageStressLevel"),
                maximum = Int(summary, "maxStressLevel"),
                restSeconds = Int(summary, "restStressDuration"),
                lowSeconds = Int(summary, "lowStressDuration"),
                mediumSeconds = Int(summary, "mediumStressDuration"),
                highSeconds = Int(summary, "highStressDuration")
            },
            garminResponse = WithoutArrays(JsonNode.Parse(text))
        };
    }

    // A fixture day as the sync posts it.
    private static object DailyFormBatch(string date) => DailyFormBatch(date, [FixtureTrainingLoad(date)],
        FixtureVO2maxEstimates(date), [FixtureSleepNight(date)], [FixtureDailySummary(date)]);

    private static object DailyFormBatch(string date, object[] trainingLoads, object[] vo2maxEstimates,
        object[]? sleepNights = null, object[]? dailySummaries = null) => new
    {
        version = 4,
        activityRange = new { fromDate = date, toDate = date },
        activities = Array.Empty<object>(),
        sleepNights = sleepNights ?? [],
        dailySummaries = dailySummaries ?? [],
        trainingLoads,
        vo2maxEstimates
    };

    // Mapped as the sync maps it: the one device's weekly load and tunnel verbatim, the
    // payload archived with its arrays removed.
    private static object FixtureTrainingLoad(string date)
    {
        var text = Fixture($"metrics-service--trainingstatus-aggregated__date-{date}.json");
        using var fixture = JsonDocument.Parse(text);
        var status = fixture.RootElement.GetProperty("mostRecentTrainingStatus").GetProperty("latestTrainingStatusData")
            .EnumerateObject().Single().Value;
        return new
        {
            calendarDate = date,
            weeklyTrainingLoad = Int(status, "weeklyTrainingLoad"),
            loadTunnelMin = Int(status, "loadTunnelMin"),
            loadTunnelMax = Int(status, "loadTunnelMax"),
            garminResponse = WithoutArrays(JsonNode.Parse(text))
        };
    }

    private static object[] FixtureVO2maxEstimates(string date)
    {
        using var fixture = JsonDocument.Parse(Fixture($"metrics-service--trainingstatus-aggregated__date-{date}.json"));
        var bySport = fixture.RootElement.GetProperty("mostRecentVO2Max");
        return new[] { "generic", "cycling" }.Select(sport => (object)new
        {
            sport,
            calendarDate = bySport.GetProperty(sport).GetProperty("calendarDate").GetString(),
            mlPerKgMin = bySport.GetProperty(sport).GetProperty("vo2MaxPreciseValue").GetDouble()
        }).ToArray();
    }

    private sealed record HeartRateZoneInput(int ZoneNumber, int LowBoundaryBpm);
    private sealed record SportProfileInput(string Name, string? TrainingMethod, int? RestingHeartRateBpm,
        int? MaxHeartRateBpm, int? LactateThresholdHeartRateBpm, IReadOnlyList<HeartRateZoneInput> HeartRateZones);
    private sealed record TimeInHeartRateZoneInput(int ZoneNumber, double Seconds);
    private sealed record CardioActivityInput(long ActivityId, string StartTimeUtc, string TypeKey, double DurationSeconds,
        IReadOnlyList<TimeInHeartRateZoneInput> TimeInHeartRateZones);

    private static object CardioBatch(IReadOnlyList<SportProfileInput> sportProfiles, params CardioActivityInput[] activities) => new
    {
        version = 2,
        activityRange = new { fromDate = "2026-08-30", toDate = "2026-09-24" },
        sportProfiles,
        activities
    };

    // Mapped as the sync maps them: numbered fields read as found, Garmin's names for the bpm figures made explicit.
    private static SportProfileInput[] FixtureSportProfiles()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../fixtures/garmin/biometric-service--heartRateZones.json"))));
        int? Bpm(JsonElement entry, string name) =>
            entry.GetProperty(name).ValueKind == JsonValueKind.Null ? null : entry.GetProperty(name).GetInt32();
        return fixture.RootElement.EnumerateArray().Select(entry => new SportProfileInput(
            entry.GetProperty("sport").GetString()!, entry.GetProperty("trainingMethod").GetString(),
            Bpm(entry, "restingHeartRateUsed"), Bpm(entry, "maxHeartRateUsed"), Bpm(entry, "lactateThresholdHeartRateUsed"),
            entry.EnumerateObject().Where(field => field.Name.StartsWith("zone") && field.Name.EndsWith("Floor"))
                .Select(field => new HeartRateZoneInput(int.Parse(field.Name[4..^5]), field.Value.GetInt32()))
                .OrderBy(zone => zone.ZoneNumber).ToArray())).ToArray();
    }

    private static CardioActivityInput FixtureCardioActivity(long activityId)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../fixtures/garmin/activitylist-service--search-activities__page-00.json"))));
        var entry = fixture.RootElement.EnumerateArray().Single(item => item.GetProperty("activityId").GetInt64() == activityId);
        return new CardioActivityInput(activityId,
            entry.GetProperty("startTimeGMT").GetString()!.Replace(' ', 'T') + "Z",
            entry.GetProperty("activityType").GetProperty("typeKey").GetString()!,
            entry.GetProperty("duration").GetDouble(),
            entry.EnumerateObject().Where(field => field.Name.StartsWith("hrTimeInZone_") && field.Value.ValueKind != JsonValueKind.Null)
                .Select(field => new TimeInHeartRateZoneInput(int.Parse(field.Name["hrTimeInZone_".Length..]), field.Value.GetDouble()))
                .OrderBy(zone => zone.ZoneNumber).ToArray());
    }

    private sealed record ExerciseSetInput(string SetType, int? Repetitions = null, decimal? WeightKg = null,
        bool? Bodyweight = null, object? Exercise = null, int? CandidateCount = null,
        double? TopProbability = null, string? StartTimeUtc = null, double? DurationSeconds = null,
        int? WktStepIndex = null);

    private static object StrengthBatch(IReadOnlyList<ExerciseSetInput>? exerciseSets) => new
    {
        version = 2,
        activityRange = new { fromDate = "2026-09-21", toDate = "2026-09-22" },
        activities = new[] { new
        {
            activityId = 24444892080L, startTimeUtc = "2026-09-21T14:34:01Z", typeKey = "strength_training",
            durationSeconds = 2820.37890625, distanceMeters = 0.0, totalSets = 14, activeSets = 14,
            totalReps = 82, exerciseSets
        } }
    };

    // The seam derives its batches from the fixture corpus: grams become kilograms and
    // Garmin's -1 weight becomes a stated bodyweight load at this mapping, as in the sync.
    private static ExerciseSetInput[] FixtureExerciseSets(long activityId)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, $"../../../../../fixtures/garmin/activity-service--exerciseSets__activity-{activityId}.json"))));
        return fixture.RootElement.GetProperty("exerciseSets").EnumerateArray().Select(entry =>
        {
            var weight = entry.GetProperty("weight").ValueKind == JsonValueKind.Null
                ? (decimal?)null : entry.GetProperty("weight").GetDecimal();
            var bodyweight = weight == -1m;
            var candidates = entry.GetProperty("exercises");
            var top = candidates.GetArrayLength() == 0 ? (JsonElement?)null : candidates[0];
            return new ExerciseSetInput(
                entry.GetProperty("setType").GetString()!,
                entry.GetProperty("repetitionCount").ValueKind == JsonValueKind.Null
                    ? null : entry.GetProperty("repetitionCount").GetInt32(),
                bodyweight || weight is null ? null : weight / 1000m,
                bodyweight,
                top is null ? null : new
                {
                    category = top.Value.GetProperty("category").GetString(),
                    name = top.Value.GetProperty("name").ValueKind == JsonValueKind.Null
                        ? null : top.Value.GetProperty("name").GetString()
                },
                candidates.GetArrayLength(),
                top?.GetProperty("probability").GetDouble(),
                entry.GetProperty("startTime").ValueKind == JsonValueKind.Null
                    ? null : entry.GetProperty("startTime").GetString() + "Z",
                entry.GetProperty("duration").ValueKind == JsonValueKind.Null
                    ? null : entry.GetProperty("duration").GetDouble(),
                entry.GetProperty("wktStepIndex").ValueKind == JsonValueKind.Null
                    ? null : entry.GetProperty("wktStepIndex").GetInt32());
        }).ToArray();
    }

    private static JsonElement Collection(JsonElement response, string name) =>
        Assert.Single(response.GetProperty("collections").EnumerateArray(),
            item => item.GetProperty("name").GetString() == name);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private readonly object gate = new();
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
                return current;
        }

        public void Set(DateTimeOffset value)
        {
            lock (gate)
                current = value;
        }
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
