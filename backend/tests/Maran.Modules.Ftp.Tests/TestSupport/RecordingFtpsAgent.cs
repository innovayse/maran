using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentFtpsClient"/> that records what the panel sent it and answers with whatever
/// the test arranged, so a handler can be measured against the WIRE rather than against a mock's
/// expectations.
/// </summary>
public sealed class RecordingFtpsAgent : IAgentFtpsClient
{
    /// <summary>The arguments of the last <see cref="EnableAsync"/> call, or null if there was none.</summary>
    public EnableFtpsArguments? LastEnableRequest { get; private set; }

    /// <summary>How many times <see cref="ReloadTlsAsync"/> has been called.</summary>
    public int ReloadTlsCalls { get; private set; }

    /// <summary>How many times <see cref="DisableAsync"/> has been called.</summary>
    public int DisableCalls { get; private set; }

    /// <summary>The hostname the last <see cref="GetStatusAsync"/> asked about, or null if there was none.</summary>
    public string? LastStatusHostname { get; private set; }

    /// <summary>The observation every daemon call answers with, unless a failure is arranged.</summary>
    public FtpsStatusDto NextStatus { get; set; } = new(
        Running: true,
        ControlPortAnswered: true,
        CertificatePresent: true,
        CertificateIsSelfSigned: false,
        CertificatePath: "/etc/maran/certificates/ftp.example.test",
        PassivePortMin: 30000,
        PassivePortMax: 30099,
        Ipv4Only: false,
        ForcedTls: true);

    /// <summary>The failure <see cref="EnableAsync"/> answers with, or null to answer with the observation.</summary>
    public Error? NextEnableError { get; set; }

    /// <summary>The failure <see cref="ReloadTlsAsync"/> answers with, or null to answer with the observation.</summary>
    public Error? NextReloadError { get; set; }

    /// <summary>How many times <see cref="CreateUserAsync"/> has been called.</summary>
    /// <remarks>
    /// A COUNT and not a flag, because the assertion that matters on the refusing paths is
    /// <c>0</c> — that the host was never reached at all — and a flag cannot distinguish "not
    /// called" from "called and reset".
    /// </remarks>
    public int CreateUserCalls { get; private set; }

    /// <summary>How the last <see cref="CreateUserAsync"/> addressed the agent, or null if there was none.</summary>
    public FtpsUserCall? LastCreateUserRequest { get; private set; }

    /// <summary>The password the last <see cref="CreateUserAsync"/> carried, or null if there was none.</summary>
    /// <remarks>
    /// Kept apart from <see cref="LastCreateUserRequest"/> so a test can assert the value the panel
    /// minted actually travelled, without a printable password joining a record that a failing
    /// assertion would render.
    /// </remarks>
    public SensitiveString? LastCreateUserPassword { get; private set; }

    /// <summary>How many times <see cref="DeleteUserAsync"/> has been called.</summary>
    public int DeleteUserCalls { get; private set; }

    /// <summary>How the last <see cref="DeleteUserAsync"/> addressed the agent, or null if there was none.</summary>
    public FtpsUserCall? LastDeleteUserRequest { get; private set; }

    /// <summary>How many times <see cref="SetPasswordAsync"/> has been called.</summary>
    public int SetPasswordCalls { get; private set; }

    /// <summary>How the last <see cref="SetPasswordAsync"/> addressed the agent, or null if there was none.</summary>
    public FtpsUserCall? LastSetPasswordRequest { get; private set; }

    /// <summary>The password the last <see cref="SetPasswordAsync"/> carried, or null if there was none.</summary>
    public SensitiveString? LastSetPasswordValue { get; private set; }

    /// <summary>The failure <see cref="CreateUserAsync"/> answers with, or null to answer with the login name.</summary>
    public Error? NextCreateUserError { get; set; }

    /// <summary>The failure <see cref="DeleteUserAsync"/> answers with, or null to succeed.</summary>
    public Error? NextDeleteUserError { get; set; }

    /// <summary>The failure <see cref="SetPasswordAsync"/> answers with, or null to succeed.</summary>
    public Error? NextSetPasswordError { get; set; }

    /// <inheritdoc />
    public Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        LastEnableRequest = new EnableFtpsArguments(
            hostname, passivePortMin, passivePortMax, passiveAddress, maxClients);

        if (NextEnableError is not null)
        {
            return Task.FromResult(Result<FtpsStatusDto>.Fail(NextEnableError));
        }

        return Task.FromResult(Result<FtpsStatusDto>.Ok(NextStatus));
    }

    /// <inheritdoc />
    public Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        DisableCalls++;
        return Task.FromResult(Result<FtpsStatusDto>.Ok(NextStatus));
    }

    /// <inheritdoc />
    public Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        LastStatusHostname = hostname;
        return Task.FromResult(Result<FtpsStatusDto>.Ok(NextStatus));
    }

    /// <inheritdoc />
    public Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        ReloadTlsCalls++;

        if (NextReloadError is not null)
        {
            return Task.FromResult(Result<FtpsStatusDto>.Fail(NextReloadError));
        }

        return Task.FromResult(Result<FtpsStatusDto>.Ok(NextStatus));
    }

    /// <inheritdoc />
    /// <remarks>
    /// It answers with the PREFIXED login, which is what the real agent reports: the panel records
    /// the agent's answer rather than rebuilding it, so a double that echoed the bare suffix would
    /// make that distinction invisible to every test.
    /// </remarks>
    public Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        CreateUserCalls++;
        LastCreateUserRequest = new FtpsUserCall(arguments.AccountUsername, arguments.FtpsUsername);
        LastCreateUserPassword = arguments.Password;

        if (NextCreateUserError is not null)
        {
            return Task.FromResult(Result<string>.Fail(NextCreateUserError));
        }

        return Task.FromResult(
            Result<string>.Ok($"{arguments.AccountUsername}_{arguments.FtpsUsername}"));
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        SetPasswordCalls++;
        LastSetPasswordRequest = new FtpsUserCall(accountUsername, ftpsUsername);
        LastSetPasswordValue = password;

        if (NextSetPasswordError is not null)
        {
            return Task.FromResult(Result<bool>.Fail(NextSetPasswordError));
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        DeleteUserCalls++;
        LastDeleteUserRequest = new FtpsUserCall(accountUsername, ftpsUsername);

        if (NextDeleteUserError is not null)
        {
            return Task.FromResult(Result<bool>.Fail(NextDeleteUserError));
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Arranges for the next enable to report that the host chose the IPv4-only fallback.</summary>
    public void NextEnableAnswersIpv4Only()
    {
        NextStatus = NextStatus with { Ipv4Only = true };
    }
}
