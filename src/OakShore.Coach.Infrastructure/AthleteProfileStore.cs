using OakShore.Coach.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace OakShore.Coach.Infrastructure;

public sealed class AthleteProfileStore(IConfiguration configuration) : DbContext
{
    internal DbSet<AthleteProfileRow> AthleteProfiles => Set<AthleteProfileRow>();
    internal DbSet<ConstraintRow> Constraints => Set<ConstraintRow>();
    internal DbSet<ActivityRow> Activities => Set<ActivityRow>();
    internal DbSet<ExerciseSetRow> ExerciseSets => Set<ExerciseSetRow>();
    internal DbSet<SportProfileRow> SportProfiles => Set<SportProfileRow>();
    internal DbSet<HeartRateZoneRow> HeartRateZones => Set<HeartRateZoneRow>();
    internal DbSet<TimeInHeartRateZoneRow> TimeInHeartRateZones => Set<TimeInHeartRateZoneRow>();
    internal DbSet<IngestBatchRow> IngestBatches => Set<IngestBatchRow>();

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

        var constraint = modelBuilder.Entity<ConstraintRow>();
        constraint.ToTable("Constraints");
        constraint.HasKey(row => new { row.UserId, row.ConstraintId });
        constraint.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        constraint.Property(row => row.ConstraintId).ValueGeneratedOnAdd();
        constraint.Property(row => row.Description).HasMaxLength(500);
        constraint.HasOne<AthleteProfileRow>().WithMany().HasForeignKey(row => row.UserId);

        var activity = modelBuilder.Entity<ActivityRow>();
        activity.ToTable("Activities");
        activity.HasKey(row => new { row.UserId, row.ActivityId });
        activity.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        activity.Property(row => row.TypeKey).HasMaxLength(100);
        activity.Property(row => row.ActivityName).HasMaxLength(500);
        activity.Property(row => row.SportProfileName).HasMaxLength(50).UseCollation("Latin1_General_100_BIN2");
        activity.HasOne<SportProfileRow>().WithMany()
            .HasForeignKey(row => new { row.UserId, row.SportProfileName, row.SportProfileRevision });

        var exerciseSet = modelBuilder.Entity<ExerciseSetRow>();
        exerciseSet.ToTable("ExerciseSets");
        exerciseSet.HasKey(row => new { row.UserId, row.ActivityId, row.Position });
        exerciseSet.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        exerciseSet.Property(row => row.SetType).HasMaxLength(50);
        exerciseSet.Property(row => row.WeightKg).HasPrecision(18, 4);
        exerciseSet.Property(row => row.ExerciseCategory).HasMaxLength(100);
        exerciseSet.Property(row => row.ExerciseName).HasMaxLength(100);
        exerciseSet.HasOne<ActivityRow>().WithMany().HasForeignKey(row => new { row.UserId, row.ActivityId });

        var sportProfile = modelBuilder.Entity<SportProfileRow>();
        sportProfile.ToTable("SportProfiles");
        sportProfile.HasKey(row => new { row.UserId, row.Name, row.Revision });
        sportProfile.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        sportProfile.Property(row => row.Name).HasMaxLength(50).UseCollation("Latin1_General_100_BIN2");
        sportProfile.Property(row => row.TrainingMethod).HasMaxLength(50);

        var heartRateZone = modelBuilder.Entity<HeartRateZoneRow>();
        heartRateZone.ToTable("HeartRateZones");
        heartRateZone.HasKey(row => new { row.UserId, row.SportProfileName, row.SportProfileRevision, row.ZoneNumber });
        heartRateZone.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        heartRateZone.Property(row => row.SportProfileName).HasMaxLength(50).UseCollation("Latin1_General_100_BIN2");
        heartRateZone.HasOne<SportProfileRow>().WithMany()
            .HasForeignKey(row => new { row.UserId, row.SportProfileName, row.SportProfileRevision });

        var timeInZone = modelBuilder.Entity<TimeInHeartRateZoneRow>();
        timeInZone.ToTable("TimeInHeartRateZones");
        timeInZone.HasKey(row => new { row.UserId, row.ActivityId, row.ZoneNumber });
        timeInZone.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        timeInZone.HasOne<ActivityRow>().WithMany().HasForeignKey(row => new { row.UserId, row.ActivityId });

        var batch = modelBuilder.Entity<IngestBatchRow>();
        batch.ToTable("IngestBatches");
        batch.HasKey(row => new { row.UserId, row.BatchHash });
        batch.Property(row => row.UserId).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        batch.Property(row => row.BatchHash).HasMaxLength(64).IsUnicode(false);
        batch.Property(row => row.FromDate).HasColumnType("date");
        batch.Property(row => row.ToDate).HasColumnType("date");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in new[]
        {
            "OakShore.Coach.Infrastructure.Migrations.0001_InitialAthleteProfile.sql",
            "OakShore.Coach.Infrastructure.Migrations.0002_StableFactsAndGoal.sql",
            "OakShore.Coach.Infrastructure.Migrations.0003_PrioritizedGoals.sql",
            "OakShore.Coach.Infrastructure.Migrations.0004_TemporaryConstraints.sql",
            "OakShore.Coach.Infrastructure.Migrations.0005_Activities.sql",
            "OakShore.Coach.Infrastructure.Migrations.0006_ActivityName.sql",
            "OakShore.Coach.Infrastructure.Migrations.0007_ExerciseSets.sql",
            "OakShore.Coach.Infrastructure.Migrations.0008_HeartRateZones.sql"
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

internal sealed class ConstraintRow
{
    public string UserId { get; set; } = "";
    public long ConstraintId { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
}

internal sealed class ActivityRow
{
    public string UserId { get; set; } = "";
    public long ActivityId { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public string TypeKey { get; set; } = "";
    public double DurationSeconds { get; set; }
    public double? DistanceMeters { get; set; }
    public int? TotalSets { get; set; }
    public int? ActiveSets { get; set; }
    public int? TotalReps { get; set; }
    public string? ActivityName { get; set; }
    public string? SportProfileName { get; set; }
    public int? SportProfileRevision { get; set; }
}

internal sealed class ExerciseSetRow
{
    public string UserId { get; set; } = "";
    public long ActivityId { get; set; }
    public int Position { get; set; }
    public string SetType { get; set; } = "";
    public int? Repetitions { get; set; }
    public decimal? WeightKg { get; set; }
    public bool Bodyweight { get; set; }
    public string? ExerciseCategory { get; set; }
    public string? ExerciseName { get; set; }
    public int? CandidateCount { get; set; }
    public double? TopProbability { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public double? DurationSeconds { get; set; }
    public int? WktStepIndex { get; set; }
}

internal sealed class SportProfileRow
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Revision { get; set; }
    public string? TrainingMethod { get; set; }
    public int? RestingHeartRateBpm { get; set; }
    public int? MaxHeartRateBpm { get; set; }
    public int? LactateThresholdHeartRateBpm { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }

    public SportProfile ToSportProfile(IEnumerable<HeartRateZoneRow> heartRateZones) => new(
        Name, TrainingMethod, RestingHeartRateBpm, MaxHeartRateBpm, LactateThresholdHeartRateBpm,
        heartRateZones.OrderBy(row => row.ZoneNumber).Select(row => new HeartRateZone(row.ZoneNumber, row.LowBoundaryBpm)).ToArray());
}

internal sealed class HeartRateZoneRow
{
    public string UserId { get; set; } = "";
    public string SportProfileName { get; set; } = "";
    public int SportProfileRevision { get; set; }
    public int ZoneNumber { get; set; }
    public int LowBoundaryBpm { get; set; }
}

internal sealed class TimeInHeartRateZoneRow
{
    public string UserId { get; set; } = "";
    public long ActivityId { get; set; }
    public int ZoneNumber { get; set; }
    public double Seconds { get; set; }
}

internal sealed class IngestBatchRow
{
    public string UserId { get; set; } = "";
    public string BatchHash { get; set; } = "";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class AthleteProfileRepository(AthleteProfileStore store, TimeProvider clock) : IAthleteProfileRepository
{
    public async Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await store.AthleteProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (row is null)
            return new AthleteProfileResponse(null, null);
        var now = clock.GetUtcNow();
        var constraints = await store.Constraints.AsNoTracking()
            .Where(item => item.UserId == userId && item.ValidFrom <= now && now < item.ValidUntil)
            .OrderBy(item => item.ConstraintId)
            .Select(item => new Constraint(item.Description, item.ValidFrom, item.ValidUntil))
            .ToArrayAsync(cancellationToken);
        return new AthleteProfileResponse(ToProfile(row) with { Constraints = constraints }, row.LastSyncedAt);
    }

    public async Task<AthleteProfileResponse> SaveAsync(string userId, IReadOnlyList<AthleteProfileChange> changes, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await store.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var savedConstraints = await ApplyChangesAsync(userId, changes, lastSyncedAt, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = await GetAsync(userId, cancellationToken);
        return response with { SavedConstraints = savedConstraints };
    }

    internal async Task<Constraint[]> ApplyChangesAsync(string userId, IReadOnlyList<AthleteProfileChange> changes,
        DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
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
        var savedConstraints = changes.Where(change => change.Constraint is not null)
            .Select(change => change.Constraint!.ToConstraint()).ToArray();
        foreach (var constraint in savedConstraints)
            store.Constraints.Add(new ConstraintRow
            {
                UserId = userId,
                Description = constraint.Description,
                ValidFrom = constraint.ValidFrom,
                ValidUntil = constraint.ValidUntil
            });
        return savedConstraints;
    }

    private static AthleteProfile ToProfile(AthleteProfileRow row) => new(
        row.UserId,
        row.BodyWeightKg,
        JsonSerializer.Deserialize<string[]>(row.AvailableEquipment) ?? [],
        row.IntendedTrainingFrequencyPerWeek,
        row.IntendedTrainingDurationMinutes,
        JsonSerializer.Deserialize<string[]>(row.LastingLimitations) ?? [],
        JsonSerializer.Deserialize<Goal[]>(row.GoalsJson) ?? [],
        row.GoalsTargetDate,
        []);
}
