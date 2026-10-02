using System.ComponentModel;
using System.Text.Json;
using OakShore.Coach.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OakShore.Coach.Api;

[McpServerToolType]
public sealed class AthleteProfileTools(AthleteProfileService profiles, SyncStatusService syncStatus, IHttpContextAccessor context)
{
    [McpServerTool(Name = "get_sync_status", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SyncStatusResponse))]
    [Description("Read stored record counts, last sync times, and the most recent Activity summary, including its stored ExerciseSets.")]
    public async Task<CallToolResult> GetSyncStatus(CancellationToken cancellationToken)
    {
        var userId = context.HttpContext!.User.FindFirst("sub")!.Value;
        var response = await syncStatus.GetAsync(userId, cancellationToken);
        return new CallToolResult
        {
            Content = [],
            StructuredContent = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    [McpServerTool(Name = "get_athlete_profile", ReadOnly = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AthleteProfileResponse))]
    [Description("Read saved AthleteProfile facts, prioritized Goals, active Constraints, and when they were last synced.")]
    public async Task<CallToolResult> GetAthleteProfile(CancellationToken cancellationToken)
    {
        var userId = context.HttpContext!.User.FindFirst("sub")!.Value;
        var response = await profiles.GetAsync(userId, cancellationToken);
        return Result(response);
    }

    [McpServerTool(Name = "update_athlete_profile", ReadOnly = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AthleteProfileResponse))]
    [Description("Save supplied AthleteProfile facts or add a temporary Constraint; return saved values and added Constraints.")]
    public async Task<CallToolResult> UpdateAthleteProfile(
        CancellationToken cancellationToken,
        [Description("Positive kilograms, at most one decimal place.")] decimal? bodyWeightKg = null,
        [Description("Available equipment as a free-text list; an empty list clears it.")] IReadOnlyList<string?>? availableEquipment = null,
        [Description("Intended training frequency, 1 to 21 times per week.")] int? intendedTrainingFrequencyPerWeek = null,
        [Description("Intended training duration, 1 to 1440 minutes.")] int? intendedTrainingDurationMinutes = null,
        [Description("Lasting limitations as a free-text list; an empty list clears it.")] IReadOnlyList<string?>? lastingLimitations = null,
        [Description("Goals in priority order, highest first; an empty list clears them.")] IReadOnlyList<GoalInput?>? goals = null,
        [Description("One shared target date for the Goals in YYYY-MM-DD, today or later (UTC).")] string? goalsTargetDate = null,
        [Description("Add a Constraint with description, validFrom and validUntil as UTC timestamps ending in Z. Start is inclusive; end is exclusive.")] ConstraintInput? constraint = null)
    {
        try
        {
            return Result(await profiles.UpdateAsync(new AthleteProfileChange(bodyWeightKg, availableEquipment,
                intendedTrainingFrequencyPerWeek, intendedTrainingDurationMinutes, lastingLimitations,
                goals, goalsTargetDate, Constraint: constraint), cancellationToken));
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
