using System.Net.Http.Json;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class AthleteProfileIngestGateway(HttpClient client) : IAthleteProfileIngestGateway
{
    public async Task<AthleteProfileResponse> SendAsync(AthleteProfileBatch batch, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/ingest", batch, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AthleteProfileResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ingest returned no saved AthleteProfile.");
    }
}
