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
