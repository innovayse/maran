using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;
using Maran.Sdk.Extensions;
using Maran.Sdk.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>
/// Holds the kind a translator RAISES for a code equal to the kind the census
/// (<see cref="ExpectedErrorStatuses.Kinds"/>) decided for it, so the two cannot drift apart again
/// (issue #299).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this pins.</b> The census said <c>RestoreStreamDropped</c> and
/// <c>RestoreStreamIdle</c> were <see cref="ErrorType.Unavailable"/> while
/// <c>RestoreAgentErrorTranslator</c> raised them as <see cref="ErrorType.Failure"/>. Every census
/// test passed, because the census proves a code HAS a decided kind and
/// <see cref="ApiResultExtensionsStatusCodeTests"/> proves a kind maps to a status; nothing compared
/// the kind a code is RAISED with to the kind it was decided to carry.
/// </para>
/// <para>
/// <b>What this closes, and what it does not.</b> It drives the restore translator through every
/// ending it names itself plus the two renames of an agent failure, and compares each result with
/// the census. It does not follow the call site of every <c>Error.Of(...)</c> in the tree: a code
/// constructed in a handler is observed by that module's handler tests, and a general sweep would
/// need the compiler's help. A translator whose output set is closed and enumerable, like this one,
/// is the shape that CAN be checked, and the next such translator adds its rows to
/// <see cref="RaisedByTranslators"/>.
/// </para>
/// <para>
/// <b>The refusing control.</b> <see cref="Disagreements"/> is exercised on a planted disagreement
/// as well, because a comparison that always returned nothing would let the real check pass in
/// silence.
/// </para>
/// </remarks>
public sealed class RaisedErrorKindsMatchCensusTests
{
    /// <summary>Endings of a restore stream that carry no error, so the translator names them itself.</summary>
    private static readonly BackupRestoreEventKind[] SelfNamedEndings =
        Enum.GetValues<BackupRestoreEventKind>()
            .Where(kind =>
            {
                return kind != BackupRestoreEventKind.Progress
                    && kind != BackupRestoreEventKind.Restored
                    && kind != BackupRestoreEventKind.Failed;
            })
            .ToArray();

    /// <summary>Every error the restore translator can raise on its own, built from real stream events.</summary>
    /// <returns>The raised errors, one per ending and per renamed agent failure.</returns>
    private static List<Error> RaisedByTranslators()
    {
        var raised = SelfNamedEndings
            .Select(kind =>
            {
                return RestoreAgentErrorTranslator.ToFailure(new BackupRestoreEvent(kind, 0, string.Empty, null, null));
            })
            .ToList();

        foreach (var agentFailure in new[]
        {
            Error.Of("AgentValidationFailed", ErrorType.Validation),
            Error.Of("AgentAlreadyExists", ErrorType.Conflict),
        })
        {
            raised.Add(RestoreAgentErrorTranslator.ToFailure(
                new BackupRestoreEvent(BackupRestoreEventKind.Failed, 0, string.Empty, null, agentFailure)));
        }

        return raised;
    }

    /// <summary>A kind the translator raises is the kind the census decided for that code.</summary>
    [Fact]
    public void The_restore_translator_raises_the_kind_the_census_decided()
    {
        var raised = RaisedByTranslators();

        var disagreements = Disagreements(raised);

        Assert.True(raised.Count >= 6, $"Only {raised.Count} raised errors were observed; the probe is blind.");
        Assert.True(disagreements.Count == 0, string.Join("; ", disagreements));
    }

    /// <summary>The comparison reports a planted disagreement, so a silent pass is not blindness.</summary>
    [Fact]
    public void A_raised_kind_that_differs_from_the_census_is_reported()
    {
        var planted = new[] { Error.Of("RestoreStreamDropped", ErrorType.Failure) };

        var disagreements = Disagreements(planted);

        Assert.Single(disagreements);
        Assert.Contains("RestoreStreamDropped", disagreements[0], StringComparison.Ordinal);
    }

    /// <summary>A dropped or idle restore stream answers 503, through the real response translation.</summary>
    /// <param name="kind">The stream ending.</param>
    /// <param name="expectedCode">The code it is named by.</param>
    [Theory]
    [InlineData(BackupRestoreEventKind.Dropped, "RestoreStreamDropped")]
    [InlineData(BackupRestoreEventKind.Idle, "RestoreStreamIdle")]
    public void A_dropped_or_idle_restore_stream_answers_service_unavailable(
        BackupRestoreEventKind kind,
        string expectedCode)
    {
        var failure = RestoreAgentErrorTranslator.ToFailure(new BackupRestoreEvent(kind, 0, string.Empty, null, null));

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var response = Assert.IsType<ObjectResult>(Result<string>.Fail(failure).ToActionResult(context));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
    }

    /// <summary>Lists every raised error whose kind is not the one the census decided for its code.</summary>
    /// <param name="raised">The errors a translator produced.</param>
    /// <returns>One sentence per disagreement or per code the census does not know; empty when all agree.</returns>
    private static List<string> Disagreements(IEnumerable<Error> raised)
    {
        var found = new List<string>();

        foreach (var error in raised)
        {
            if (!ExpectedErrorStatuses.Kinds.TryGetValue(error.Code, out var decided))
            {
                found.Add($"{error.Code} is raised but the census has no kind for it");
            }
            else if (decided != error.Type)
            {
                found.Add($"{error.Code} is raised as {error.Type} but the census decided {decided}");
            }
        }

        return found;
    }
}
