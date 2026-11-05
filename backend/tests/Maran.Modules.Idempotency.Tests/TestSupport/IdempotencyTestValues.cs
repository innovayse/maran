namespace Maran.Modules.Idempotency.Tests.TestSupport;

/// <summary>The caller keys, keys and hashes the tests share, so each reads as a name and not a literal.</summary>
public static class IdempotencyTestValues
{
    /// <summary>One administrator's caller key.</summary>
    public const string AdministratorA = "user:11111111-1111-4111-8111-111111111111";

    /// <summary>Another principal's caller key.</summary>
    public const string AdministratorB = "user:22222222-2222-4222-8222-222222222222";

    /// <summary>A key the client chose.</summary>
    public const string Key = "create-account-2026-11-05.attempt:1";

    /// <summary>SHA-256 hex of one request.</summary>
    public const string HashOne = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>SHA-256 hex of a different request.</summary>
    public const string HashTwo = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
}
