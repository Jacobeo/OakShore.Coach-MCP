using System.Net.Http.Json;
using System.Net;
using OakShore.Coach.Domain;

namespace OakShore.Coach.Infrastructure;

public sealed class AthleteProfileIngestGateway(HttpClient client) : IAthleteProfileIngestGateway
{
    public async Task<AthleteProfileResponse> SendAsync(IngestBatch batch, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/ingest", batch, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var rejection = await response.Content.ReadFromJsonAsync<IngestRejection>(cancellationToken);
            throw new ArgumentException(rejection?.Error ?? "Ingest rejected the AthleteProfile change.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AthleteProfileResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ingest returned no saved AthleteProfile.");
    }

    private sealed record IngestRejection(string Error);
}
