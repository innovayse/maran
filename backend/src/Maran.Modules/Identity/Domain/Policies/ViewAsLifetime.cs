namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>How long a view-as session, and the one access token issued for it, may be used.</summary>
/// <remarks>
/// <para>
/// <b>Fifteen minutes, and a constant rather than an option.</b> A view-as token is a bearer
/// credential for ANOTHER person, and it has no refresh step at which anything is re-checked. Long
/// enough to look through an account's screens and follow one support thread; short enough that a
/// tab left open ends itself. It equals the default access-token lifetime, so a view-as token never
/// outlives what an ordinary token could. Making it configurable would let one edit of a settings
/// file turn it into a day-long credential for somebody else; a longer look is a second start, and
/// every start is journalled.
/// </para>
/// <para>
/// The reasoning is recorded in
/// <c>docs/superpowers/notes/2026-11-02-view-as-impersonation-threat-note.md</c>.
/// </para>
/// </remarks>
public static class ViewAsLifetime
{
    /// <summary>The lifetime of a view-as session and of its access token.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
}
