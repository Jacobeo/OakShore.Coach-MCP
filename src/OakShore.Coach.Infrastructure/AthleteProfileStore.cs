using OakShore.Coach.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace OakShore.Coach.Infrastructure;

public sealed class AthleteProfileStore(IConfiguration configuration) : DbContext
{
    internal DbSet<AthleteProfileRow> AthleteProfiles => Set<AthleteProfileRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlServer(configuration["Persistence:ConnectionString"]
            ?? throw new InvalidOperationException("Persistence:ConnectionString is required."));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<AthleteProfileRow>();
        profile.ToTable("AthleteProfiles");
        profile.HasKey(row => row.UserId);
        // OAuth subject identifiers are case-sensitive.
        profile.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        profile.Property(row => row.BodyWeightKg).HasPrecision(18, 1);
        profile.Property(row => row.GoalsTargetDate).HasColumnType("date");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in new[]
        {
            "OakShore.Coach.Infrastructure.Migrations.0001_InitialAthleteProfile.sql",
            "OakShore.Coach.Infrastructure.Migrations.0002_StableFactsAndGoal.sql",
            "OakShore.Coach.Infrastructure.Migrations.0003_PrioritizedGoals.sql"
        })
        {
            await using var stream = typeof(AthleteProfileStore).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing schema migration {resource}.");
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync(cancellationToken);
            await Database.ExecuteSqlRawAsync(script, cancellationToken);
        }
    }
}

internal sealed class AthleteProfileRow
{
    public string UserId { get; set; } = "";
    public decimal? BodyWeightKg { get; set; }
    public string AvailableEquipment { get; set; } = "[]";
    public int? IntendedTrainingFrequencyPerWeek { get; set; }
    public int? IntendedTrainingDurationMinutes { get; set; }
    public string LastingLimitations { get; set; } = "[]";
    public string GoalsJson { get; set; } = "[]";
    public DateOnly? GoalsTargetDate { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class AthleteProfileRepository(AthleteProfileStore store) : IAthleteProfileRepository
{
    public async Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await store.AthleteProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        return row is null
            ? new AthleteProfileResponse(null, null)
            : new AthleteProfileResponse(ToProfile(row), row.LastSyncedAt);
    }

    public async Task<AthleteProfileResponse> SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> changes, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await store.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var row = await store.AthleteProfiles.SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new AthleteProfileRow { UserId = userId };
            store.AthleteProfiles.Add(row);
        }
        var profile = changes.Aggregate(ToProfile(row), (current, change) => current.Apply(change));
        row.BodyWeightKg = profile.BodyWeightKg;
        row.AvailableEquipment = JsonSerializer.Serialize(profile.AvailableEquipment);
        row.IntendedTrainingFrequencyPerWeek = profile.IntendedTrainingFrequencyPerWeek;
        row.IntendedTrainingDurationMinutes = profile.IntendedTrainingDurationMinutes;
        row.LastingLimitations = JsonSerializer.Serialize(profile.LastingLimitations);
        row.GoalsJson = JsonSerializer.Serialize(profile.Goals);
        row.GoalsTargetDate = profile.GoalsTargetDate;
        row.LastSyncedAt = lastSyncedAt;
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AthleteProfileResponse(profile, lastSyncedAt);
    }

    private static AthleteProfile ToProfile(AthleteProfileRow row) => new(
        row.UserId,
        row.BodyWeightKg,
        JsonSerializer.Deserialize<string[]>(row.AvailableEquipment) ?? [],
        row.IntendedTrainingFrequencyPerWeek,
        row.IntendedTrainingDurationMinutes,
        JsonSerializer.Deserialize<string[]>(row.LastingLimitations) ?? [],
        JsonSerializer.Deserialize<Goal[]>(row.GoalsJson) ?? [],
        row.GoalsTargetDate);
}
