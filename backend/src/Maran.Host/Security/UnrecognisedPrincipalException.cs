namespace Maran.Host.Security;

/// <summary>
/// An authenticated request whose token cannot be read as an administrator or a customer.
/// </summary>
/// <remarks>
/// A bug or a version skew, never an input error: the token verified, so the panel issued it, and
/// the panel only issues the two roles. It is raised instead of being answered with an anonymous or
/// a customer caller, because either would be a silent misreading of who is signed in — an unknown
/// role treated as anonymous hides a broken issuer behind "not signed in", and one treated as a
/// customer invents a tenant. The message names what is wrong with the token, never a claim value.
/// </remarks>
public sealed class UnrecognisedPrincipalException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the token's claims; carries no claim value.</param>
    public UnrecognisedPrincipalException(string message)
        : base(message)
    {
    }
}
