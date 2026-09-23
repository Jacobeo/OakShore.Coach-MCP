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
    [Description("Read your saved body weight and when it was last synced.")]
    public async Task<CallToolResult> GetAthleteProfile(CancellationToken cancellationToken)
    {
        var userId = context.HttpContext!.User.FindFirst("sub")!.Value;
        var response = await profiles.GetAsync(userId, cancellationToken);
        return Result(response);
    }

    [McpServerTool(Name = "update_athlete_profile", ReadOnly = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AthleteProfileResponse))]
    [Description("Save your body weight in kilograms and return the saved value.")]
    public async Task<CallToolResult> UpdateAthleteProfile(
        [Description("Positive body weight in kilograms, with at most one decimal place.")] decimal bodyWeightKg,
        CancellationToken cancellationToken)
    {
        try
        {
            return Result(await profiles.UpdateAsync(bodyWeightKg, cancellationToken));
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
