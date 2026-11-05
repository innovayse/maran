namespace Maran.SharedKernel.Callers;

/// <summary>
/// Who a request or message runs as: nobody, an API key, an administrator, a reseller or a customer — a closed set.
/// </summary>
/// <remarks>
/// <para>
/// The kind of caller is one fact, and it used to be spread over five fields that had to be read
/// together to be understood: an empty id for nobody, an empty name for nobody, a missing account for
/// both an administrator and nobody, a flag for the administrator, and a second pair of fields for an
/// operator viewing as somebody. Every consumer re-derived the case from those, and each stand-in
/// (<c>Guid.Empty</c>, <c>""</c>, <c>null</c>) was a value a caller could mistake for a real one. This
/// type closes the set instead — six cases: <see cref="AnonymousCaller"/>, <see cref="ApiKeyCaller"/> (a
/// machine authenticated by an API key: not a login, so no user id, role or account),
/// <see cref="AdministratorCaller"/>, <see cref="ResellerCaller"/>, <see cref="CustomerCaller"/> and
/// <see cref="AccountlessCustomerCaller"/> (a customer login with no account to act for — a real state,
/// not a missing value), and a member exists only on the case that has it — an account id on a customer
/// acting for one, a reseller id on a reseller and a key id on an API key, nowhere else.
/// </para>
/// <para>
/// The hierarchy is closed by construction: the constructor is <c>private protected</c>, so no
/// assembly but this one can add a case, and a <c>switch</c> over them is complete.
/// </para>
/// </remarks>
public abstract record Caller
{
    /// <summary>Seals the hierarchy to this assembly.</summary>
    private protected Caller()
    {
    }
}
