using System.ComponentModel;
using System.Text.Json;
using OakShore.Coach.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OakShore.Coach.Api;

[McpServerToolType]
public sealed class AthleteProfileTools(AthleteProfileService profiles, IHttpContextAccessor context)
{
    [McpServerTool(Name = "get_athlete_profile", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AthleteProfileResponse))]
    [Description("Read saved AthleteProfile facts, Goal, and when they were last synced.")]
    public async Task<CallToolResult> GetAthleteProfile(CancellationToken cancellationToken)
    {
        var userId = context.HttpContext!.User.FindFirst("sub")!.Value;
        var response = await profiles.GetAsync(userId, cancellationToken);
        return Result(response);
    }

    [McpServerTool(Name = "update_athlete_profile", ReadOnly = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AthleteProfileResponse))]
    [Description("Save supplied AthleteProfile facts and return the saved values.")]
    public async Task<CallToolResult> UpdateAthleteProfile(
        CancellationToken cancellationToken,
        [Description("Positive kilograms, at most one decimal place.")] decimal? bodyWeightKg = null,
        [Description("Available equipment as a free-text list; an empty list clears it.")] IReadOnlyList<string?>? availableEquipment = null,
        [Description("Intended training frequency, 1 to 21 times per week.")] int? intendedTrainingFrequencyPerWeek = null,
        [Description("Intended training duration, 1 to 1440 minutes.")] int? intendedTrainingDurationMinutes = null,
        [Description("Lasting limitations as a free-text list; an empty list clears it.")] IReadOnlyList<string?>? lastingLimitations = null,
        [Description("Goal description and targetDate in YYYY-MM-DD, today or later (UTC).")] GoalChange? goal = null)
    {
        try
        {
            return Result(await profiles.UpdateAsync(new AthleteProfileChange(bodyWeightKg, availableEquipment,
                intendedTrainingFrequencyPerWeek, intendedTrainingDurationMinutes, lastingLimitations, goal), cancellationToken));
        }
        catch (ArgumentException error)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [],
                StructuredContent = JsonSerializer.SerializeToElement(new { error = error.Message, lastSyncedAt = (DateTimeOffset?)null })
            };
        }
    }

    private static CallToolResult Result(AthleteProfileResponse response)
    {
        return new CallToolResult
        {
            Content = [],
            StructuredContent = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }
}
