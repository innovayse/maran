using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.DbService;

/// <summary>Stub of <c>IDbServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test. A stub that records a request nothing asserts is
/// worse than one that records nothing: it makes the mapping look covered while every field of it is
/// free to change.
/// </remarks>
internal sealed class StubDbService : IDbServiceInvoker
{
    /// <summary>Response returned from <see cref="CreateDatabaseAsync"/>.</summary>
    public CreateDatabaseResponse CreateResponse { get; set; } = new();

    /// <summary>The last creation request the stub received, for asserting the mapping.</summary>
    public CreateDatabaseRequest? LastCreateRequest { get; private set; }

    /// <summary>Response returned from <see cref="DropDatabaseAsync"/>.</summary>
    public DropDatabaseResponse DropResponse { get; set; } = new();

    /// <summary>The last drop request the stub received, for asserting the mapping.</summary>
    public DropDatabaseRequest? LastDropRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetDatabasePasswordAsync"/>.</summary>
    public SetDatabasePasswordResponse SetPasswordResponse { get; set; } = new();

    /// <summary>The last password-set request the stub received, for asserting the mapping.</summary>
    public SetDatabasePasswordRequest? LastSetPasswordRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListDatabasesAsync"/>.</summary>
    public ListDatabasesResponse ListResponse { get; set; } = new();

    /// <summary>The last listing request the stub received, for asserting the mapping.</summary>
    public ListDatabasesRequest? LastListRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetDatabaseSizeAsync"/>.</summary>
    public GetDatabaseSizeResponse SizeResponse { get; set; } = new();

    /// <summary>The last size request the stub received, for asserting the mapping.</summary>
    public GetDatabaseSizeRequest? LastSizeRequest { get; private set; }

    /// <summary>Response returned from <see cref="RepairDatabaseGrantsAsync"/>.</summary>
    public RepairDatabaseGrantsResponse RepairGrantsResponse { get; set; } = new();

    /// <summary>The last grant-repair request the stub received, for asserting the report-only flag.</summary>
    public RepairDatabaseGrantsRequest? LastRepairGrantsRequest { get; private set; }

    /// <summary>The answer returned for a <c>CreateDatabaseUser</c> call.</summary>
    public CreateDatabaseUserResponse CreateUserResponse { get; set; } = new();

    /// <summary>The last <c>CreateDatabaseUser</c> request received.</summary>
    public CreateDatabaseUserRequest? LastCreateUserRequest { get; private set; }

    /// <summary>The answer returned for a <c>DropDatabaseUser</c> call.</summary>
    public DropDatabaseUserResponse DropUserResponse { get; set; } = new();

    /// <summary>The last <c>DropDatabaseUser</c> request received.</summary>
    public DropDatabaseUserRequest? LastDropUserRequest { get; private set; }

    /// <summary>The answer returned for a <c>SetDatabaseUserPrivileges</c> call.</summary>
    public SetDatabaseUserPrivilegesResponse SetPrivilegesResponse { get; set; } = new();

    /// <summary>The last <c>SetDatabaseUserPrivileges</c> request received.</summary>
    public SetDatabaseUserPrivilegesRequest? LastSetPrivilegesRequest { get; private set; }

    /// <summary>Builds a stub whose creation call fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubDbService FailingCreateWith(ErrorCode code, string message)
    {
        return new StubDbService
        {
            CreateResponse = new CreateDatabaseResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <inheritdoc/>
    public Task<CreateDatabaseResponse> CreateDatabaseAsync(
        CreateDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        return Task.FromResult(CreateResponse);
    }

    /// <inheritdoc/>
    public Task<DropDatabaseResponse> DropDatabaseAsync(
        DropDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        LastDropRequest = request;
        return Task.FromResult(DropResponse);
    }

    /// <summary>Builds a stub whose password-set call fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubDbService FailingSetPasswordWith(ErrorCode code, string message)
    {
        return new StubDbService
        {
            SetPasswordResponse = new SetDatabasePasswordResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <inheritdoc/>
    public Task<SetDatabasePasswordResponse> SetDatabasePasswordAsync(
        SetDatabasePasswordRequest request,
        CancellationToken cancellationToken)
    {
        LastSetPasswordRequest = request;
        return Task.FromResult(SetPasswordResponse);
    }

    /// <inheritdoc/>
    public Task<CreateDatabaseUserResponse> CreateDatabaseUserAsync(
        CreateDatabaseUserRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateUserRequest = request;
        return Task.FromResult(CreateUserResponse);
    }

    /// <inheritdoc/>
    public Task<DropDatabaseUserResponse> DropDatabaseUserAsync(
        DropDatabaseUserRequest request,
        CancellationToken cancellationToken)
    {
        LastDropUserRequest = request;
        return Task.FromResult(DropUserResponse);
    }

    /// <inheritdoc/>
    public Task<SetDatabaseUserPrivilegesResponse> SetDatabaseUserPrivilegesAsync(
        SetDatabaseUserPrivilegesRequest request,
        CancellationToken cancellationToken)
    {
        LastSetPrivilegesRequest = request;
        return Task.FromResult(SetPrivilegesResponse);
    }

    /// <inheritdoc/>
    public Task<ListDatabasesResponse> ListDatabasesAsync(
        ListDatabasesRequest request,
        CancellationToken cancellationToken)
    {
        LastListRequest = request;
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public Task<GetDatabaseSizeResponse> GetDatabaseSizeAsync(
        GetDatabaseSizeRequest request,
        CancellationToken cancellationToken)
    {
        LastSizeRequest = request;
        return Task.FromResult(SizeResponse);
    }

    /// <inheritdoc/>
    public Task<RepairDatabaseGrantsResponse> RepairDatabaseGrantsAsync(
        RepairDatabaseGrantsRequest request,
        CancellationToken cancellationToken)
    {
        LastRepairGrantsRequest = request;
        return Task.FromResult(RepairGrantsResponse);
    }
}
