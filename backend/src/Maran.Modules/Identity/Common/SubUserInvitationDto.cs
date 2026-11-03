namespace Maran.Modules.Identity.Common;

/// <summary>
/// The one answer to "invite this person", whatever became of the request.
/// </summary>
/// <remarks>
/// It echoes what the owner typed and nothing the server knows. Whether the login name or the
/// address already belongs to somebody on this server must not be readable from the answer
/// (a cross-tenant enumeration), so a created invitation and a withheld one are indistinguishable
/// here by status, by body shape and by field values; the real outcome reaches the invited address,
/// the owner's list of pending people, and the administrator's audit log.
/// </remarks>
/// <param name="Username">The login name the owner asked for, as typed.</param>
/// <param name="Email">The address the owner asked the invitation to be sent to, as typed.</param>
public sealed record SubUserInvitationDto(string Username, string Email);
