namespace OakShore.Coach.Domain;

public interface IAthleteProfileIngestGateway
{
    Task<AthleteProfileResponse> SendAsync(AthleteProfileBatch batch, CancellationToken cancellationToken);
}
