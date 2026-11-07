using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// Builds audit rows, key derivers and sealed chains for the chain tests, so each test states only the
/// one thing it is about.
/// </summary>
public static class AuditChainFixture
{
    /// <summary>A throwaway base64 256-bit panel secret; the real one is the deriver's own concern.</summary>
    public const string Secret = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>A different base64 256-bit panel secret, standing for a rotated one.</summary>
    public const string OtherSecret = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";

    /// <summary>The instant the first row of a seeded journal happened.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a key deriver over a panel secret.</summary>
    /// <param name="secret">The base64 secret; the default is <see cref="Secret"/>.</param>
    /// <returns>The deriver the sealer and verifier are given.</returns>
    public static HkdfSubKeyDeriver Deriver(string secret = Secret)
    {
        return new HkdfSubKeyDeriver(secret);
    }

    /// <summary>Builds one unsealed row with a fixed shape; every argument can be overridden.</summary>
    /// <param name="at">When the row happened; the default is <see cref="Start"/>.</param>
    /// <param name="action">The action column.</param>
    /// <param name="subject">The subject column.</param>
    /// <param name="actorUserId">The actor's id; a fresh one when omitted.</param>
    /// <param name="impersonator">The operator viewing as the actor, or <c>null</c>.</param>
    /// <param name="accountId">The account the row concerns, or <c>null</c>.</param>
    /// <param name="id">The row's id; a fresh one when omitted.</param>
    /// <returns>An unsealed row.</returns>
    public static AuditEvent Row(
        DateTimeOffset? at = null,
        string action = "Login",
        string subject = "alice",
        Guid? actorUserId = null,
        AuditImpersonator? impersonator = null,
        Guid? accountId = null,
        Guid? id = null)
    {
        return new AuditEvent(
            id ?? Guid.NewGuid(),
            at ?? Start,
            actorUserId ?? Guid.NewGuid(),
            "alice",
            action,
            subject,
            "203.0.113.7",
            "agent/1.0",
            succeeded: true,
            correlationId: "corr-1",
            impersonator,
            accountId);
    }

    /// <summary>Stores <paramref name="count"/> unsealed rows, one minute apart, oldest first.</summary>
    /// <param name="context">The context to store them in.</param>
    /// <param name="count">How many rows.</param>
    /// <returns>The rows, in the order they happened.</returns>
    public static async Task<List<AuditEvent>> SeedAsync(IdentityDbContext context, int count)
    {
        var rows = Enumerable.Range(0, count)
            .Select(index =>
            {
                return Row(Start.AddMinutes(index), subject: "subject-" + index);
            })
            .ToList();

        context.AuditEvents.AddRange(rows);
        await context.SaveChangesAsync();

        return rows;
    }

    /// <summary>Stores <paramref name="count"/> rows and seals all of them.</summary>
    /// <param name="context">The context to store them in.</param>
    /// <param name="count">How many rows.</param>
    /// <param name="secret">The panel secret the chain key is derived from.</param>
    /// <returns>The sealed rows in chain order, tracked by the context.</returns>
    public static async Task<List<AuditEvent>> SeedSealedAsync(IdentityDbContext context, int count, string secret = Secret)
    {
        await SeedAsync(context, count);

        await new AuditChainSealer(context, Deriver(secret)).SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        // The sealer detaches what it seals, so the rows are read again and a test can edit or remove them.
        return await context.AuditEvents.OrderBy(row => row.ChainIndex).ToListAsync();
    }

    /// <summary>Builds a verifier over a context.</summary>
    /// <param name="context">The context holding the journal.</param>
    /// <param name="secret">The panel secret the chain key is derived from.</param>
    /// <returns>A verifier with its own gate.</returns>
    public static AuditChainVerifier Verifier(IdentityDbContext context, string secret = Secret)
    {
        return new AuditChainVerifier(context, Deriver(secret), new AuditChainVerificationGate());
    }
}
