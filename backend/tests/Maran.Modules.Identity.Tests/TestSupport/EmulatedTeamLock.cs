using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The sub-user team lock as this project can run it: a transaction whose rollback removes the
/// logins, memberships, membership offers and tokens written since it began, without the PostgreSQL row lock the real
/// one is built on.
/// </summary>
/// <remarks>
/// <para>
/// It is a stand-in because the real lock's whole content is a PostgreSQL facility (<c>FOR UPDATE</c>
/// inside an explicit transaction) and the provider these tests run on has neither: using the real
/// one would throw on its first statement. The rollback is emulated so the handler's decoy can be
/// observed to leave nothing behind, which is the part of the contract this provider can show.
/// </para>
/// <para>
/// <b>UNOBSERVED HERE: that two simultaneous invitations cannot both pass the cap, and that a real
/// rollback undoes the writes.</b> Both are properties of PostgreSQL, invisible to any test on this
/// provider, and are measured by <c>Maran.Host.IntegrationTests.SubUserInviteCapRaceTests</c>.
/// </para>
/// </remarks>
public sealed class EmulatedTeamLock : ISubUserTeamLock
{
    /// <summary>The module's database, snapshotted at the start and cleaned on a rollback.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>How many transactions were committed.</summary>
    public int Commits { get; private set; }

    /// <summary>How many transactions were rolled back.</summary>
    public int Rollbacks { get; private set; }

    /// <summary>How many rows the rollbacks removed in total (logins, memberships, offers and tokens).</summary>
    public int RowsRolledBack { get; private set; }

    /// <summary>Creates the stand-in.</summary>
    /// <param name="dbContext">The module's database.</param>
    public EmulatedTeamLock(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users.Select(row => row.Id).ToListAsync(cancellationToken);
        var memberships = await _dbContext.AccountMemberships.Select(row => row.Id).ToListAsync(cancellationToken);
        var tokens = await _dbContext.InvitationTokens.Select(row => row.Id).ToListAsync(cancellationToken);
        var offers = await _dbContext.AccountMembershipOffers.Select(row => row.Id).ToListAsync(cancellationToken);

        return new EmulatedTransaction(this, users, memberships, tokens, offers);
    }

    /// <summary>A transaction that remembers what existed when it began.</summary>
    private sealed class EmulatedTransaction : IDbContextTransaction
    {
        /// <summary>The lock that handed this transaction out, whose counters it updates.</summary>
        private readonly EmulatedTeamLock _owner;

        /// <summary>The login ids that existed at the start.</summary>
        private readonly List<Guid> _users;

        /// <summary>The membership ids that existed at the start.</summary>
        private readonly List<Guid> _memberships;

        /// <summary>The invitation token ids that existed at the start.</summary>
        private readonly List<Guid> _tokens;

        /// <summary>The membership offer ids that existed at the start.</summary>
        private readonly List<Guid> _offers;

        /// <inheritdoc />
        public Guid TransactionId { get; } = Guid.NewGuid();

        /// <summary>Creates the transaction.</summary>
        /// <param name="owner">The lock that handed it out.</param>
        /// <param name="users">Login ids at the start.</param>
        /// <param name="memberships">Membership ids at the start.</param>
        /// <param name="tokens">Token ids at the start.</param>
        /// <param name="offers">Membership offer ids at the start.</param>
        public EmulatedTransaction(
            EmulatedTeamLock owner,
            List<Guid> users,
            List<Guid> memberships,
            List<Guid> tokens,
            List<Guid> offers)
        {
            _owner = owner;
            _users = users;
            _memberships = memberships;
            _tokens = tokens;
            _offers = offers;
        }

        /// <inheritdoc />
        public void Commit()
        {
            _owner.Commits++;
        }

        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Commit();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Rollback()
        {
            RollbackAsync().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            var db = _owner._dbContext;
            var users = await db.Users.Where(row => !_users.Contains(row.Id)).ToListAsync(cancellationToken);
            var memberships = await db.AccountMemberships
                .Where(row => !_memberships.Contains(row.Id)).ToListAsync(cancellationToken);
            var tokens = await db.InvitationTokens.Where(row => !_tokens.Contains(row.Id)).ToListAsync(cancellationToken);
            var offers = await db.AccountMembershipOffers.Where(row => !_offers.Contains(row.Id)).ToListAsync(cancellationToken);
            db.AccountMembershipOffers.RemoveRange(offers);
            db.InvitationTokens.RemoveRange(tokens);
            db.AccountMemberships.RemoveRange(memberships);
            db.Users.RemoveRange(users);
            await db.SaveChangesAsync(cancellationToken);

            _owner.Rollbacks++;
            _owner.RowsRolledBack += users.Count + memberships.Count + tokens.Count + offers.Count;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
