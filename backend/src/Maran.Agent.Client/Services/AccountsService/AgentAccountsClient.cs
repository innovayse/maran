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
                new AccountSuspensionStateDto(
                    response.Ok.LoginLocked,
                    // Mapped by NUMBER and not by name: the wire enum and this project's are two
                    // declarations of the same four states, and a value this build has never heard
                    // of — a newer agent — must read as Unspecified, which sends the caller back to
                    // LoginLocked rather than to whatever the cast produced.
                    Enum.IsDefined(typeof(AccountLoginPasswordState), (int)response.Ok.LoginPasswordState)
                        ? (AccountLoginPasswordState)(int)response.Ok.LoginPasswordState
                        : AccountLoginPasswordState.Unspecified,
                    response.Ok.SitesDirectoryReadable,
                    response.Ok.Sites
                        .Select(site => { return new SiteSuspensionFactDto(site.Domain, site.ServingStub); })
                        .ToList(),
                    response.Ok.CronEntriesTotal,
                    response.Ok.CronEntriesSuspended,
                    response.Ok.CronForeignLines,
                    // The seam where two vocabularies meet, and it is NOT a bug: the wire field is
                    // sftp_logins and the message SftpLoginSuspensionFact, names they keep for ever
                    // because the contract evolves additively (rules/architecture.md) and they
                    // predate FTPS. The list itself has carried logins of BOTH transfer daemons
                    // since FTPS shipped, so this project's own DTO is named for the pair —
                    // FileTransferLogins / FileTransferLoginSuspensionFactDto. Renaming what the
                    // panel received is what a DTO layer is for; renaming what was sent is not.
                    response.Ok.SftpLogins
                        .Select(login =>
                        {
                            return new FileTransferLoginSuspensionFactDto(
                                login.Username,
                                login.Locked,

                                // By NUMBER and with the same guard the password state gets: a
                                // protocol a newer agent knows and this build does not must read as
                                // Unspecified, which a caller resolves to SFTP, rather than as a
                                // cast to an enum member that does not exist.
                                Enum.IsDefined(typeof(LoginTransferProtocol), (int)login.Protocol)
                                    ? (LoginTransferProtocol)(int)login.Protocol
                                    : LoginTransferProtocol.Unspecified);
                        })
                        .ToList(),

                    // The count of what the enumeration above deliberately could not speak for.
                    // Carried rather than dropped: an attestation is worth what it says about its
                    // own gaps, and this is the only field that says anything about them.
                    response.Ok.UnmanagedLogins)),
            GetAccountSuspensionStateResponse.ResultOneofCase.Error => Result<AccountSuspensionStateDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetSuspensionStateAsync))),
            _ => Result<AccountSuspensionStateDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
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
}
