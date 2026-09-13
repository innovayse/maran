namespace Maran.Sdk.Contracts;

/// <summary>
/// Builds the one <see cref="AuditEntry"/> shape every module uses for work the panel did on its
/// own initiative — a nightly renewal, a brute-force ban, an alert raised off a timer — so that a
/// module cannot invent its own spelling of "nobody signed in did this".
/// </summary>
/// <remarks>
/// <para>
/// It lives beside <see cref="AuditEntry"/> in <c>Maran.Sdk/Contracts</c> rather than in
/// <c>Maran.SharedKernel/Utilities/</c> because it constructs an <see cref="AuditEntry"/>, and
/// SharedKernel references nothing of ours — the contract type is not visible there. The reference
/// direction decides the home, not the fact that the code itself is plain BCL.
/// </para>
/// <para>
/// Three properties are fixed here once, and each was spelt differently by a different module
/// before: the actor id is <c>null</c>, which is the contract's documented value for "nobody could
/// be identified" — <see cref="Guid.Empty"/> is a valid-looking id in an indexed column and reads
/// as a user that simply has no rows; the actor name is <c>maran-&lt;module&gt;</c>, which no login
/// can hold because account names are validated Linux user names; and the address and client
/// columns are EMPTY, because nothing arrived over HTTP. Putting the actor name in those two
/// columns makes the journal answer "where did this come from" with a lie, and their doc comments
/// on <see cref="AuditEntry"/> say plainly what they are for.
/// </para>
/// </remarks>
public static class SystemAuditEntry
{
    /// <summary>The prefix every system actor name carries, marking the panel itself as the actor.</summary>
    public const string NamePrefix = "maran-";

    /// <summary>The actor name a module's unattended work is recorded under.</summary>
    /// <param name="module">The module's short lowercase name, such as <c>ssl</c> or <c>firewall</c>.</param>
    /// <returns>The name, as <c>maran-&lt;module&gt;</c>.</returns>
    public static string NameFor(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        return NamePrefix + module;
    }

    /// <summary>Builds the journal entry for something the panel did with no signed-in caller.</summary>
    /// <param name="module">The module acting, as passed to <see cref="NameFor"/>.</param>
    /// <param name="action">What was done; one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What it was done to — a domain, an address, a service.</param>
    /// <param name="succeeded">Whether it took effect. A refused unattended action is the entry worth reading.</param>
    /// <returns>An entry with no actor id and no request origin.</returns>
    public static AuditEntry Create(string module, string action, string subject, bool succeeded)
    {
        return new AuditEntry(
            null,
            NameFor(module),
            action,
            subject,
            string.Empty,
            string.Empty,
            succeeded);
    }
}
