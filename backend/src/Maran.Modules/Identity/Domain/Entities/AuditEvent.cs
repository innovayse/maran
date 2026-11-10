using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// One row of the audit journal: who did what, when, from where, and whether it worked (spec §10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three things may happen to a row after it is written, and each is a named method.</b>
/// <see cref="Seal"/> attaches the tamper-evidence proof once; <see cref="ErasePerson"/> and
/// <see cref="EraseAccount"/> replace the columns that identify a person or an account with a
/// pseudonym. Nothing else changes a row: the setters are private, and the columns that say WHAT
/// happened and WHEN (<see cref="Id"/>, <see cref="OccurredAt"/>, <see cref="Action"/>,
/// <see cref="Succeeded"/>, <see cref="CorrelationId"/>) are not touched by any method.
/// </para>
/// <para>
/// The columns split into two sets, and the split is the design. The IMMUTABLE set is what the chain
/// hash covers directly. The ERASABLE set (actor id and name, subject, address, user agent, account
/// id, impersonator) is covered only through a salted commitment, so erasing it by destroying the
/// salt leaves the chain verifiable. Which columns are in which set is decided by the chain's
/// canonical form, not here; this type only guarantees that the erase methods touch nothing
/// outside the erasable set.
/// </para>
/// </remarks>
public sealed class AuditEvent
{
    /// <summary>The width of <see cref="ActorUsername"/>, in characters.</summary>
    /// <remarks>
    /// A login name, and long enough for one. It is NOT long enough for every value that reaches
    /// it: a claimed name may be an email address, which the standard allows 320 characters. The
    /// constant lives here so that the configuration and the writer that must respect it read the
    /// same number, rather than each restating a literal that the other can outgrow.
    /// </remarks>
    public const int ActorUsernameMaxLength = 64;

    /// <summary>The width of <see cref="Action"/>, in characters.</summary>
    public const int ActionMaxLength = 64;

    /// <summary>The width of <see cref="Subject"/>, in characters.</summary>
    public const int SubjectMaxLength = 256;

    /// <summary>The width of <see cref="IpAddress"/>, in characters: a full IPv4-mapped IPv6 text.</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>The width of <see cref="CorrelationId"/>, in characters.</summary>
    public const int CorrelationIdMaxLength = 64;

    /// <summary>The length, in bytes, of <see cref="RowHash"/> and <see cref="ErasableCommitment"/>: SHA-256 and HMAC-SHA256 output.</summary>
    public const int HashLength = 32;

    /// <summary>The length, in bytes, of <see cref="CommitmentSalt"/>.</summary>
    public const int SaltLength = 16;

    /// <summary>The event's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who did it; null when the actor could not be identified.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>The name the actor used.</summary>
    public string ActorUsername { get; private set; }

    /// <summary>What was attempted.</summary>
    public string Action { get; private set; }

    /// <summary>What it was attempted on.</summary>
    public string Subject { get; private set; }

    /// <summary>Where it came from.</summary>
    public string IpAddress { get; private set; }

    /// <summary>What client it came from.</summary>
    public string UserAgent { get; private set; }

    /// <summary>Whether it worked.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>The request's correlation id, tying this row to the logs.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// The operator who was viewing the panel as the actor when this happened (issue #317); null when
    /// the actor was acting as themselves. The actor columns keep the login whose authority the action
    /// ran under, and this value says who was really at the keyboard.
    /// </summary>
    public AuditImpersonator? Impersonator { get; private set; }

    /// <summary>
    /// The hosting account this entry is about, when the writer knew one: a customer session, a
    /// viewed-as session, or an Accounts-module entry that named it. Null for everything else,
    /// including administrator entries of the other journals. Erasable: account erasure replaces it
    /// with a random id that points at nothing.
    /// </summary>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// The verified login this entry is ABOUT, when it was done to one (an owner changing a sub-user's
    /// grants); null for every entry whose subject is only text somebody typed. Erasable: erasing the
    /// login replaces it with the pseudonym id. It is what ties a subject to a person, because the
    /// subject text alone is chosen by the caller and identifies nobody.
    /// </summary>
    public Guid? SubjectUserId { get; private set; }

    /// <summary>
    /// The row's position in the tamper-evidence chain; null until the maintenance worker seals it.
    /// Positions start at 1 and never repeat (a unique index holds that).
    /// </summary>
    public long? ChainIndex { get; private set; }

    /// <summary>
    /// The keyed hash that links this row to the previous one (HMAC-SHA256, <see cref="HashLength"/>
    /// bytes); null until sealed.
    /// </summary>
    public byte[]? RowHash { get; private set; }

    /// <summary>
    /// A salted SHA-256 commitment to the erasable columns as they were when the row was sealed
    /// (<see cref="HashLength"/> bytes); null until sealed. It stays after erasure: it is what lets
    /// the row hash survive the columns it covers being replaced.
    /// </summary>
    public byte[]? ErasableCommitment { get; private set; }

    /// <summary>
    /// The random salt of <see cref="ErasableCommitment"/> (<see cref="SaltLength"/> bytes). Null on
    /// an unsealed row and, deliberately, on an erased one: destroying it is what makes the erased
    /// values unrecoverable from the commitment.
    /// </summary>
    public byte[]? CommitmentSalt { get; private set; }

    /// <summary>When the first erasure touched this row; null when no erasure has.</summary>
    public DateTimeOffset? ErasedAt { get; private set; }

    /// <summary>Whether the row has been given its place in the chain.</summary>
    public bool IsSealed
    {
        get { return ChainIndex is not null; }
    }

    /// <summary>Whether an erasure has replaced any of the row's erasable columns.</summary>
    public bool IsErased
    {
        get { return ErasedAt is not null; }
    }

    /// <summary>Records an event.</summary>
    /// <param name="id">The event's identity.</param>
    /// <param name="occurredAt">When it happened, taken from <see cref="IClock"/>.</param>
    /// <param name="actorUserId">Who did it; null when the actor could not be identified, as on a failed login.</param>
    /// <param name="actorUsername">The name the actor used, which exists even when the user does not.</param>
    /// <param name="action">What was attempted, from <c>*AuditActions</c>.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="ipAddress">Where it came from.</param>
    /// <param name="userAgent">What client it came from.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="correlationId">The request's correlation id, tying this row to the logs.</param>
    /// <param name="impersonator">The operator viewing as the actor, or null when there was none.</param>
    /// <param name="accountId">The hosting account the entry is about, or null when the writer knew none.</param>
    /// <param name="subjectUserId">The verified login the entry is about, or null when the subject is only typed text.</param>
    public AuditEvent(
        Guid id,
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string actorUsername,
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        string? correlationId,
        AuditImpersonator? impersonator = null,
        Guid? accountId = null,
        Guid? subjectUserId = null)
    {
        Id = id;
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        ActorUsername = actorUsername;
        Action = action;
        Subject = subject;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Succeeded = succeeded;
        CorrelationId = correlationId;
        Impersonator = impersonator;
        AccountId = accountId;
        SubjectUserId = subjectUserId;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AuditEvent()
    {
        ActorUsername = string.Empty;
        Action = string.Empty;
        Subject = string.Empty;
        IpAddress = string.Empty;
        UserAgent = string.Empty;
    }

    /// <summary>Gives the row its place in the tamper-evidence chain, exactly once.</summary>
    /// <param name="chainIndex">The row's position; positive, and unique across the table.</param>
    /// <param name="erasableCommitment">The salted commitment to the erasable columns, <see cref="HashLength"/> bytes.</param>
    /// <param name="commitmentSalt">
    /// The commitment's salt, <see cref="SaltLength"/> bytes; null if and only if the row is already erased,
    /// because an erased row must never hold the salt that would let its old values be checked.
    /// </param>
    /// <param name="rowHash">The keyed link to the previous row, <see cref="HashLength"/> bytes.</param>
    /// <exception cref="InvalidOperationException">The row is already sealed.</exception>
    /// <exception cref="ArgumentException">A value has the wrong shape, or the salt contradicts <see cref="IsErased"/>.</exception>
    public void Seal(long chainIndex, byte[] erasableCommitment, byte[]? commitmentSalt, byte[] rowHash)
    {
        if (IsSealed)
        {
            throw new InvalidOperationException("The audit row is already sealed.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chainIndex);
        RequireLength(erasableCommitment, HashLength, nameof(erasableCommitment));
        RequireLength(rowHash, HashLength, nameof(rowHash));

        var saltPresent = commitmentSalt is not null;

        if (saltPresent == IsErased)
        {
            throw new ArgumentException(
                "An erased row is sealed without a salt, and a row that is not erased is sealed with one.",
                nameof(commitmentSalt));
        }

        if (commitmentSalt is not null)
        {
            RequireLength(commitmentSalt, SaltLength, nameof(commitmentSalt));
            CommitmentSalt = commitmentSalt.ToArray();
        }

        ChainIndex = chainIndex;
        ErasableCommitment = erasableCommitment.ToArray();
        RowHash = rowHash.ToArray();
    }

    /// <summary>
    /// Replaces what identifies the erased person in this row with a pseudonym, and destroys the
    /// commitment's salt.
    /// </summary>
    /// <param name="erasedUserId">The login being erased, matched against the actor, the impersonator and the subject login.</param>
    /// <param name="at">When the erasure happened, from the panel's clock; kept only if no earlier erasure set it.</param>
    /// <param name="pseudonymId">The random id standing in for the person across all of their rows.</param>
    /// <param name="pseudonymName">The name standing in for the person, for example <c>erased-user-1a2b3c4d</c>.</param>
    /// <param name="ownNames">The login name, address and id the person was known by; compared with the subject of a row the person acted in.</param>
    /// <remarks>
    /// <para>
    /// <b>A row is the person's only by an identity recorded on it, never by text.</b> It matches as the
    /// ACTOR (<see cref="ActorUserId"/>), as the IMPERSONATOR, or as the SUBJECT login
    /// (<see cref="SubjectUserId"/>). A row of an unidentified caller who merely typed the person's name
    /// (a refused sign-in) carries none of these and is never touched: the typed text is chosen by
    /// whoever typed it, so letting it select rows would let anyone who can create a login with a chosen
    /// name erase other people's evidence.
    /// </para>
    /// <para>
    /// Actor columns are replaced only when the person is the actor; when an operator who is being erased
    /// viewed as somebody else, the actor columns belong to that living person and are kept. The address
    /// and user agent are the request's, so they are blanked when the person was the actor or the
    /// impersonator, and kept when the person was only the subject (they belong to whoever acted). The
    /// subject text is replaced when the person is the subject login, or when the person is the actor and
    /// the subject is one of <paramref name="ownNames"/> (a row about themselves).
    /// </para>
    /// <para>A row that matches in none of these ways is left exactly as it was, <see cref="ErasedAt"/> included.</para>
    /// </remarks>
    public void ErasePerson(
        Guid erasedUserId,
        DateTimeOffset at,
        Guid pseudonymId,
        string pseudonymName,
        IReadOnlySet<string> ownNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pseudonymName);
        ArgumentNullException.ThrowIfNull(ownNames);

        var actorMatches = ActorUserId == erasedUserId;
        var impersonatorMatches = Impersonator is not null && Impersonator.OperatorId == erasedUserId;
        var subjectMatches = SubjectUserId == erasedUserId;

        if (!actorMatches && !impersonatorMatches && !subjectMatches)
        {
            return;
        }

        if (actorMatches)
        {
            ActorUserId = pseudonymId;
            ActorUsername = pseudonymName;
        }

        if (impersonatorMatches)
        {
            Impersonator = new AuditImpersonator(pseudonymId, pseudonymName);
        }

        if (actorMatches || impersonatorMatches)
        {
            IpAddress = string.Empty;
            UserAgent = string.Empty;
        }

        if (subjectMatches || (actorMatches && ownNames.Contains(Subject)))
        {
            Subject = pseudonymName;
        }

        if (subjectMatches)
        {
            SubjectUserId = pseudonymId;
        }

        MarkErased(at);
    }

    /// <summary>Replaces what ties this row to a deleted hosting account with a pseudonym, and destroys the commitment's salt.</summary>
    /// <param name="at">When the erasure happened, from the panel's clock; kept only if no earlier erasure set it.</param>
    /// <param name="pseudonymAccountId">A fresh random id that points at no account.</param>
    /// <param name="pseudonymSubject">The text that replaces the subject, for example <c>erased-account-1a2b3c4d</c>.</param>
    /// <remarks>
    /// The actor columns are deliberately kept: an administrator's accountability for what was done
    /// to an account is not the account owner's personal data.
    /// </remarks>
    public void EraseAccount(DateTimeOffset at, Guid pseudonymAccountId, string pseudonymSubject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pseudonymSubject);

        AccountId = pseudonymAccountId;
        Subject = pseudonymSubject;
        MarkErased(at);
    }

    /// <summary>Records that an erasure touched the row and destroys the salt, keeping the earliest erasure time.</summary>
    /// <param name="at">When the erasure happened.</param>
    private void MarkErased(DateTimeOffset at)
    {
        ErasedAt ??= at;
        CommitmentSalt = null;
    }

    /// <summary>Refuses a byte array that is not exactly the expected length.</summary>
    /// <param name="value">The array to check.</param>
    /// <param name="length">The length it must have.</param>
    /// <param name="parameterName">The parameter it came from, for the exception.</param>
    private static void RequireLength(byte[] value, int length, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length != length)
        {
            throw new ArgumentException($"Expected exactly {length} bytes.", parameterName);
        }
    }
}
