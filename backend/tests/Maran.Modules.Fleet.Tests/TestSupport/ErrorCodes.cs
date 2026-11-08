namespace Maran.Modules.Fleet.Tests.TestSupport;

/// <summary>
/// The error codes this module raises, spelled once for the tests that assert them.
/// </summary>
/// <remarks>
/// The module's own code says <c>nameof(ErrorMessages.X)</c>, where <c>ErrorMessages</c> is the class
/// MSBuild generates from the neutral resx, and that class is <c>internal</c> in every shipped module,
/// so a test project cannot name it. The hazard of a spelled string is drift from the resx, and it is
/// closed by <c>ErrorMessagesTests.Every_code_this_module_raises_has_an_entry_in_all_three_locales</c>,
/// which checks every constant here against the three resx files.
/// </remarks>
public static class ErrorCodes
{
    /// <summary>The server is not in a state that allows the requested change.</summary>
    public const string ServerStateTransitionInvalid = "ServerStateTransitionInvalid";

    /// <summary>No host has enrolled for the server, so there is nothing to approve.</summary>
    public const string ServerNotEnrolled = "ServerNotEnrolled";

    /// <summary>The fingerprint typed for approval is not the one the server presented.</summary>
    public const string ServerFingerprintMismatch = "ServerFingerprintMismatch";

    /// <summary>The server the panel runs on cannot be switched off.</summary>
    public const string ServerLocalImmutable = "ServerLocalImmutable";

    /// <summary>Every code above, for the test that checks each against the resx triple.</summary>
    public static readonly string[] All =
    [
        ServerStateTransitionInvalid,
        ServerNotEnrolled,
        ServerFingerprintMismatch,
        ServerLocalImmutable,
    ];
}
