using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>Maps the agent's account rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// The agent answers failures inside the response's oneof rather than as a gRPC status,
/// because "this account already exists" is an answer the panel acts on. This client turns
/// that branch into a typed <see cref="Error"/> whose code the module maps to an HTTP status,
/// and logs the agent's own diagnostic text — which is operator-facing and must not reach a
/// customer (rules/security.md item 8).
/// </remarks>
public sealed class AgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IAccountsServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentAccountsClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentAccountsClient(IAccountsServiceInvoker invoker, ILogger<AgentAccountsClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentAccountsClient(GrpcChannel channel, ILogger<AgentAccountsClient> logger)
        : this(new GrpcAccountsServiceInvoker(new V1.AccountsService.AccountsServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        var request = new CreateAccountRequest { Username = username, QuotaBytes = quotaBytes };
        var response = await _invoker.CreateAccountAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateAccountResponse.ResultOneofCase.Ok => Result<CreatedAccountDto>.Ok(
                new CreatedAccountDto(response.Ok.HomeDirectory, response.Ok.Uid)),
            CreateAccountResponse.ResultOneofCase.Error => Result<CreatedAccountDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateAsync))),
            _ => Result<CreatedAccountDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.SuspendAccountAsync(
            new SuspendAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            SuspendAccountResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SuspendAccountResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SuspendAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.UnsuspendAccountAsync(
            new UnsuspendAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            UnsuspendAccountResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            UnsuspendAccountResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(UnsuspendAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The wire lists are copied into this module's own DTOs rather than surfaced as protobuf
    /// collections, so nothing outside this project holds a generated type — the same boundary every
    /// other method here keeps. Order is preserved as the agent reported it.
    /// </remarks>
    public async Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var response = await _invoker.GetAccountSuspensionStateAsync(
            new GetAccountSuspensionStateRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            GetAccountSuspensionStateResponse.ResultOneofCase.Ok => Result<AccountSuspensionStateDto>.Ok(
                ToSuspensionState(response.Ok)),
            GetAccountSuspensionStateResponse.ResultOneofCase.Error => Result<AccountSuspensionStateDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetSuspensionStateAsync))),
            _ => Result<AccountSuspensionStateDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the wire suspension observation onto the panel's own.</summary>
    /// <param name="ok">The success payload of <c>GetAccountSuspensionState</c>.</param>
    /// <returns>The observation, with no wire default left in it.</returns>
    private static AccountSuspensionStateDto ToSuspensionState(GetAccountSuspensionStateOk ok)
    {
        return new AccountSuspensionStateDto(
            ToLoginPasswordState(ok.LoginPasswordState, ok.LoginLocked),
            ok.SitesDirectoryReadable,
            ok.Sites
                .Select(site => { return new SiteSuspensionFactDto(site.Domain, site.ServingStub); })
                .ToList(),
            ok.CronEntriesTotal,
            ok.CronEntriesSuspended,
            ok.CronForeignLines,
            // The seam where two vocabularies meet, and it is NOT a bug: the wire field is
            // sftp_logins and the message SftpLoginSuspensionFact, names they keep for ever
            // because the contract evolves additively (rules/architecture.md) and they
            // predate FTPS. The list itself has carried logins of BOTH transfer daemons
            // since FTPS shipped, so this project's own DTO is named for the pair —
            // FileTransferLogins / FileTransferLoginSuspensionFactDto. Renaming what the
            // panel received is what a DTO layer is for; renaming what was sent is not.
            ok.SftpLogins
                .Select(login =>
                {
                    return new FileTransferLoginSuspensionFactDto(
                        login.Username,
                        login.Locked,
                        ToProtocol(login.Protocol));
                })
                .ToList(),
            ToUnmanagedLoginCount(ok));
    }

    /// <summary>Names what the account's own shadow password field holds.</summary>
    /// <param name="state">The wire classification.</param>
    /// <param name="loginLocked">The wire's older boolean, <c>passwd -S</c>'s view of the same login.</param>
    /// <returns>One of the four real states, never the contract's zero value.</returns>
    /// <remarks>
    /// The contract's zero value means the agent predates the classification, and a value this build
    /// has never heard of means the agent is newer; neither is a state of the login. Both are
    /// answered from the boolean the agent has always sent, in the one way that is conservative in
    /// both directions: a lock reads as <see cref="AccountLoginPasswordState.Locked"/>, which makes a
    /// suspension accept it and a reactivation refuse it, and no lock reads as
    /// <see cref="AccountLoginPasswordState.Usable"/>, which makes a suspension refuse and a
    /// reactivation accept — exactly what falling back to the boolean did when the handlers did it
    /// themselves, decided here once so that no handler sees a value that is not a state.
    /// </remarks>
    private static AccountLoginPasswordState ToLoginPasswordState(LoginPasswordState state, bool loginLocked)
    {
        return state switch
        {
            LoginPasswordState.Empty => AccountLoginPasswordState.Empty,
            LoginPasswordState.Absent => AccountLoginPasswordState.Absent,
            LoginPasswordState.Locked => AccountLoginPasswordState.Locked,
            LoginPasswordState.Usable => AccountLoginPasswordState.Usable,
            _ => loginLocked ? AccountLoginPasswordState.Locked : AccountLoginPasswordState.Usable,
        };
    }

    /// <summary>Names which transfer daemon serves a login.</summary>
    /// <param name="protocol">The wire protocol.</param>
    /// <returns>The daemon, never the contract's zero value.</returns>
    /// <remarks>
    /// The contract's zero value means the agent predates the field, and before FTPS existed every
    /// login a suspension could report was an SFTP login by construction, so SFTP is the correct
    /// reading rather than a default chosen for convenience. A protocol this build has never heard
    /// of (an agent newer than the panel) is read the same way: the handshake window refuses such a
    /// pair, and a login is never reported as a daemon the agent did not name.
    /// </remarks>
    private static LoginTransferProtocol ToProtocol(TransferProtocol protocol)
    {
        return protocol == TransferProtocol.Ftps ? LoginTransferProtocol.Ftps : LoginTransferProtocol.Sftp;
    }

    /// <summary>Decides whether the answer carries a measured count of the unmanaged logins.</summary>
    /// <param name="ok">The success payload of <c>GetAccountSuspensionState</c>.</param>
    /// <returns>The measured count, or the fact that the answer carries none.</returns>
    /// <remarks>
    /// <para>
    /// The wire carries a <c>uint32</c>, so an agent that predates the field looks like one that
    /// counted zero. They are told apart by the rest of the same answer: <c>unmanaged_logins</c> and
    /// the per-login <c>protocol</c> were added by one change, so an agent that states a protocol for
    /// any login also counted these entries, and a non-zero count is self-evidently a measurement. A
    /// zero count with no stated protocol is not proof, and is carried as unmeasured so that no caller
    /// prints it as the completeness claim "none exist".
    /// </para>
    /// <para>
    /// The inference errs in one direction only, deliberately: an account with no transfer login at
    /// all reads as unmeasured even when a current agent counted and found none, so the panel says
    /// less than it knows and never claims a silence it did not achieve. It reads the RAW protocol,
    /// before <see cref="ToProtocol"/> resolves an unstated one to SFTP; after that resolution the
    /// distinction no longer exists.
    /// </para>
    /// </remarks>
    private static UnmanagedLoginCount ToUnmanagedLoginCount(GetAccountSuspensionStateOk ok)
    {
        var measured = ok.UnmanagedLogins > 0
            || ok.SftpLogins.Any(login => { return login.Protocol != TransferProtocol.Unspecified; });

        return measured ? new MeasuredUnmanagedLogins(ok.UnmanagedLogins) : new UnmeasuredUnmanagedLogins();
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.DeleteAccountAsync(
            new DeleteAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            DeleteAccountResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.BytesFreed),
            DeleteAccountResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        var request = new SetAccountQuotaRequest { Username = username, QuotaBytes = quotaBytes };
        var response = await _invoker.SetAccountQuotaAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetAccountQuotaResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetAccountQuotaResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetQuotaAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken)
    {
        var request = new SetAccountResourceLimitsRequest
        {
            Username = username,
            CpuQuotaPercent = ToWire(limits.CpuQuotaPercent),
            MemoryMaxMib = ToWire(limits.MemoryMaxMib),
            TasksMax = ToWire(limits.TasksMax),
        };
        var response = await _invoker.SetAccountResourceLimitsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetAccountResourceLimitsResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetAccountResourceLimitsResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetResourceLimitsAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.GetAccountUsageAsync(
            new GetAccountUsageRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
#pragma warning disable CS0612 // QuotaBytes is deprecated on the wire but still read as the legacy mirror.
            GetAccountUsageResponse.ResultOneofCase.Ok => Result<AccountUsageDto>.Ok(
                new AccountUsageDto(
                    response.Ok.UsedBytes,
                    response.Ok.QuotaBytes,
                    Enum.IsDefined(typeof(AccountQuotaState), (int)response.Ok.QuotaState)
                        ? (AccountQuotaState)(int)response.Ok.QuotaState
                        : AccountQuotaState.Unspecified,
                    Enum.IsDefined(typeof(QuotaUnenforceableReason), (int)response.Ok.QuotaUnenforceableReason)
                        ? (QuotaUnenforceableReason)(int)response.Ok.QuotaUnenforceableReason
                        : QuotaUnenforceableReason.Unspecified)),
#pragma warning restore CS0612
            GetAccountUsageResponse.ResultOneofCase.Error => Result<AccountUsageDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetUsageAsync))),
            _ => Result<AccountUsageDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        var request = new RepairAccountHomeGroupsRequest { ReportOnly = reportOnly };
        var response = await _invoker.RepairAccountHomeGroupsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RepairAccountHomeGroupsResponse.ResultOneofCase.Ok => Result<HomeGroupRepairReportDto>.Ok(
                ToReport(response.Ok)),
            RepairAccountHomeGroupsResponse.ResultOneofCase.Error => Result<HomeGroupRepairReportDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(RepairHomeGroupsAsync))),
            _ => Result<HomeGroupRepairReportDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the wire census onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>RepairAccountHomeGroups</c>.</param>
    /// <returns>
    /// The same four buckets and the same two counts, in the order the agent sent them. Nothing is
    /// merged and nothing is dropped: the four buckets sum to <c>examined</c>, and a mapping that
    /// folded "would repair" into "repaired" would make a report-only pass indistinguishable from one
    /// that acted.
    /// </returns>
    private static HomeGroupRepairReportDto ToReport(RepairAccountHomeGroupsOk ok)
    {
        return new HomeGroupRepairReportDto(
            ok.Examined,
            ok.AlreadyCorrect,
            ok.Repaired.Select(ToRepaired).ToList(),
            ok.WouldRepair.Select(ToRepaired).ToList(),
            ok.Refused.Select(ToRefused).ToList());
    }

    /// <summary>Projects one repaired — or would-be-repaired — home.</summary>
    /// <param name="home">The wire row.</param>
    /// <returns>The panel's carrier for it.</returns>
    private static RepairedHomeDto ToRepaired(RepairedHome home)
    {
        return new RepairedHomeDto(home.AccountUsername, home.Home);
    }

    /// <summary>Projects one refused account, reason included.</summary>
    /// <param name="home">The wire row.</param>
    /// <returns>
    /// The panel's carrier for it. The account name and path are carried through UNCHANGED — a
    /// refused row is refused because it is not a home this panel's own account creation produced, so
    /// tidying it here would hide the very thing an operator has to look at.
    /// </returns>
    private static RefusedHomeDto ToRefused(RefusedHome home)
    {
        return new RefusedHomeDto(home.AccountUsername, home.Home, home.Reason.ToString());
    }

    /// <summary>States one slice ceiling in the wire's two-case shape.</summary>
    /// <param name="ceiling">The ceiling to send.</param>
    /// <returns>The wire ceiling, with exactly one of its cases set.</returns>
    private static ResourceCeiling ToWire(SliceCeiling ceiling)
    {
        return ceiling.Kind switch
        {
            SliceCeilingKind.Limited => new ResourceCeiling { Maximum = ceiling.Maximum },
            SliceCeilingKind.Unlimited => new ResourceCeiling { Unlimited = new ResourceCeilingUnlimited() },
            _ => throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling.Kind, "Unknown slice ceiling kind."),
        };
    }
}
