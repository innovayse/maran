using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>Where the HTTP-01 challenge file goes, and who writes it.</summary>
public sealed class AcmeChallengeWriterTests
{
    /// <summary>The challenge token every test here writes.</summary>
    private const string Token = "tok3n-value";

    /// <summary>The challenge is written through the agent under the owning accounts name.</summary>
    [Fact]
    public async Task The_challenge_is_written_through_the_agent_under_the_owning_accounts_name()
    {
        var files = new RecordingAgentFilesClient();

        var result = await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", Token, "key-authorization", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var write = Assert.Single(files.Writes);
        Assert.Equal("acct", write.Account);
        Assert.Equal("key-authorization", write.Content);
    }

    /// <summary>The challenge lands where the vhost serves it inside the sites document root.</summary>
    [Fact]
    public async Task The_challenge_lands_where_the_vhost_serves_it_inside_the_sites_document_root()
    {
        var files = new RecordingAgentFilesClient();

        await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", Token, "key-authorization", CancellationToken.None);

        Assert.Equal($"sites/example.com/.well-known/acme-challenge/{Token}", Assert.Single(files.Writes).Path);
    }

    /// <summary>The challenge file is world readable so the web server can serve it.</summary>
    [Fact]
    public async Task The_challenge_file_is_world_readable_so_the_web_server_can_serve_it()
    {
        var files = new RecordingAgentFilesClient();

        await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", Token, "key-authorization", CancellationToken.None);

        // 0644 — the web server runs as its own user and must be able to read it.
        Assert.Equal(0b110_100_100u, Assert.Single(files.Writes).Mode);
    }

    /// <summary>An agent that refuses the write is surfaced as a failure rather than swallowed.</summary>
    [Fact]
    public async Task An_agent_that_refuses_the_write_is_surfaced_as_a_failure_rather_than_swallowed()
    {
        var files = new RecordingAgentFilesClient(Error.Of("AgentValidationFailed", ErrorType.Validation));

        var result = await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", Token, "key-authorization", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
    }

    /// <summary>Removing deletes the same path it wrote and never recursively.</summary>
    [Fact]
    public async Task Removing_deletes_the_same_path_it_wrote_and_never_recursively()
    {
        var files = new RecordingAgentFilesClient();

        await new AcmeChallengeWriter(files)
            .RemoveAsync("acct", "example.com", Token, CancellationToken.None);

        var delete = Assert.Single(files.Deletes);
        Assert.Equal($"sites/example.com/.well-known/acme-challenge/{Token}", delete.Path);
        Assert.False(delete.Recursive);
    }

    /// <summary>A token that is not base64url is refused before any path is built from it.</summary>
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("tok/en")]
    [InlineData("tok.en")]
    [InlineData("")]
    public async Task A_token_that_is_not_base64url_is_refused_before_any_path_is_built_from_it(string token)
    {
        // The token comes from a REMOTE party and becomes a path component. RFC 8555 says it is
        // base64url, but "the specification says so" is not a boundary check — and the other half of
        // the old defence, "the agent re-validates", describes a file service that does not exist yet.
        var files = new RecordingAgentFilesClient();

        var result = await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", token, "key-authorization", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeChallengeTokenInvalid", result.Error!.Code);
        Assert.Empty(files.Writes);
    }

    /// <summary>Removal refuses the same tokens the write refuses.</summary>
    [Fact]
    public async Task Removal_refuses_the_same_tokens_the_write_refuses()
    {
        var files = new RecordingAgentFilesClient();

        var result = await new AcmeChallengeWriter(files)
            .RemoveAsync("acct", "example.com", "../../../etc/passwd", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(files.Deletes);
    }

    /// <summary>A conforming base64url token is accepted.</summary>
    [Theory]
    [InlineData("abcDEF012")]
    [InlineData("tok-3n_value")]
    public async Task A_conforming_base64url_token_is_accepted(string token)
    {
        var files = new RecordingAgentFilesClient();

        var result = await new AcmeChallengeWriter(files)
            .WriteAsync("acct", "example.com", token, "key-authorization", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(files.Writes);
    }
}
