using Coach.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Coach.Infrastructure;

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
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string resource = "Coach.Infrastructure.Migrations.0001_InitialAthleteProfile.sql";
        await using var stream = typeof(AthleteProfileStore).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing schema migration {resource}.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(cancellationToken);
        await Database.ExecuteSqlRawAsync(script, cancellationToken);
    }
}

internal sealed class AthleteProfileRow
{
    public string UserId { get; set; } = "";
    public decimal BodyWeightKg { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class AthleteProfileRepository(AthleteProfileStore store) : IAthleteProfileRepository
{
    public async Task<AthleteProfileResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await store.AthleteProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        return row is null
            ? new AthleteProfileResponse(null, null)
            : new AthleteProfileResponse(new AthleteProfile(row.UserId, row.BodyWeightKg), row.LastSyncedAt);
    }

    public async Task<AthleteProfileResponse> SaveAsync(AthleteProfile profile, DateTimeOffset lastSyncedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await store.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var row = await store.AthleteProfiles.SingleOrDefaultAsync(row => row.UserId == profile.UserId, cancellationToken);
        if (row is null)
        {
            row = new AthleteProfileRow { UserId = profile.UserId };
            store.AthleteProfiles.Add(row);
        }
        row.BodyWeightKg = profile.BodyWeightKg;
        row.LastSyncedAt = lastSyncedAt;
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AthleteProfileResponse(profile, lastSyncedAt);
    }
}
