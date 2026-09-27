namespace OakShore.Coach.Domain;

public interface IAthleteProfileIngestGateway
{
    Task<AthleteProfileResponse> SendAsync(IngestBatch batch, CancellationToken cancellationToken);
}
