namespace Maran.Modules.Identity.Queries.GetProfile;

/// <summary>Reads the signed-in person's own profile.</summary>
/// <param name="UserId">
/// Whose profile to read. Taken from the caller's own token by the controller and never from the
/// request, so there is no parameter with which to ask for somebody else's.
/// </param>
public sealed record GetProfileQuery(Guid UserId);
