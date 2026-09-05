using Maran.Sdk.Extensions;
using Maran.Sdk.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>
/// Pins the HTTP status the panel answers for every <see cref="ErrorType"/>, and holds the census
/// that stops a newly shipped error code going unclassified.
/// </summary>
/// <remarks>
/// <para>
/// <b>What changed, and why these tests are shaped differently now.</b> The status used to be
/// inferred from the spelling of the error code's suffix, with a 400 fallback, so the risk this
/// file guarded was the inference silently ceasing to match — which had already happened once,
/// answering 400 to every missing account. That inference is gone: <see cref="Error"/> carries an
/// <see cref="ErrorType"/> that the compiler demands at every construction site, and
/// <c>ApiResultExtensions</c> reads nothing but that. So the mapping under test is now seven arms
/// wide and is asserted exhaustively, and what needs guarding moved to the OTHER half — whether
/// each shipped code has had its kind decided by a person.
/// </para>
/// <para>
/// <b>Why the expectations are not computed.</b> An expectation derived from the code's name would
/// be a production rule written a second time, agreeing with itself however broken.
/// <see cref="ExpectedErrorStatuses.Kinds"/> states each code's kind as a decision, and
/// <see cref="A_codes_name_never_contradicts_its_kind"/> is a one-way check over the four suffixes
/// whose meaning is not in doubt — it catches a typo in the table without re-deriving it.
/// </para>
/// <para>
/// <b>The vacuity guard, and it is on the axis that goes blind.</b> The list of codes is not typed
/// into this file: <see cref="ErrorCodeCensus"/> reads it out of the embedded resx tables of the
/// assemblies the panel actually ships, so a NEW code fails
/// <see cref="Every_shipped_error_code_is_classified"/> until somebody writes down what kind it is.
/// The discovery itself is guarded by a positive control
/// (<see cref="Census_discovers_codes_from_every_shipping_assembly"/>): a census that found nothing
/// would otherwise make every other assertion here vacuously true.
/// </para>
/// <para>
/// <b>Blind spot, stated.</b> These assertions exercise the translation of an <see cref="Error"/>
/// into a response, and the census proves every code has a decided kind. They do NOT prove that the
/// handler which produces a given code passes the kind this table names — that link is the
/// compiler's to enforce at the call site and each module's handler tests' to observe — and they do
/// not observe the two Host codes in
/// <see cref="ExpectedErrorStatuses.AnsweredOutsideResultTranslation"/>, whose responses are
/// written by middleware this project does not run.
/// </para>
/// </remarks>
public sealed class ApiResultExtensionsStatusCodeTests
{
    /// <summary>The status each kind of failure must answer, stated as a decision per kind.</summary>
    private static readonly Dictionary<ErrorType, int> StatusOfKind =
        new Dictionary<ErrorType, int>
        {
            [ErrorType.Validation] = StatusCodes.Status400BadRequest,
            [ErrorType.NotFound] = StatusCodes.Status404NotFound,
            [ErrorType.Conflict] = StatusCodes.Status409Conflict,
            [ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
            [ErrorType.Forbidden] = StatusCodes.Status403Forbidden,
            [ErrorType.Unavailable] = StatusCodes.Status503ServiceUnavailable,
            [ErrorType.Failure] = StatusCodes.Status500InternalServerError,
        };

    /// <summary>Every kind of failure, as xUnit theory rows.</summary>
    /// <returns>One row per kind: the kind and the status it must answer.</returns>
    public static IEnumerable<object[]> Kinds()
    {
        return StatusOfKind.Select(entry => { return new object[] { entry.Key, entry.Value }; });
    }

    /// <summary>Every shipped code, as xUnit theory rows carrying the kind decided for it.</summary>
    /// <returns>One row per code: the code and its kind.</returns>
    public static IEnumerable<object[]> ClassifiedCodes()
    {
        return ExpectedErrorStatuses.Kinds.Select(entry => { return new object[] { entry.Key, entry.Value }; });
    }

    /// <summary>Each kind of failure answers the status written down for it.</summary>
    /// <param name="type">The kind of failure carried by the error.</param>
    /// <param name="expectedStatus">The HTTP status it must answer.</param>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Kind_answers_its_expected_status(ErrorType type, int expectedStatus)
    {
        var response = Translate("MaranProbeCode", type);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    /// <summary>Every value of the enum has a decided status, so a new kind cannot ship unmapped.</summary>
    /// <remarks>
    /// The mapping's own default arm answers 500 for a kind it does not know, which is the right
    /// answer at runtime and a silent one in a test. This is what makes adding a value to
    /// <see cref="ErrorType"/> fail here by name instead of quietly answering 500 in production.
    /// </remarks>
    [Fact]
    public void Every_error_type_has_a_decided_status()
    {
        var undecided = Enum.GetValues<ErrorType>()
            .Where(type => { return !StatusOfKind.ContainsKey(type); })
            .ToList();

        Assert.True(undecided.Count == 0, $"ErrorType values with no decided status: {string.Join(", ", undecided)}");
    }

    /// <summary>Every shipped error code answers the status of the kind decided for it.</summary>
    /// <param name="code">The machine-stable error code.</param>
    /// <param name="type">The kind decided for it.</param>
    [Theory]
    [MemberData(nameof(ClassifiedCodes))]
    public void Code_answers_the_status_of_its_kind(string code, ErrorType type)
    {
        var response = Translate(code, type);

        Assert.Equal(StatusOfKind[type], response.StatusCode);
    }

    /// <summary>A failure response carries the machine code that produced it.</summary>
    [Fact]
    public void Failure_response_carries_its_machine_code()
    {
        var response = Translate("SiteNotFound", ErrorType.NotFound);

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal("SiteNotFound", problem.Extensions["code"]);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    /// <summary>No code's name states one outcome while the table classifies it as another.</summary>
    /// <remarks>
    /// One-way and deliberately narrow. These four suffixes have exactly one meaning in this
    /// repository, so a code that ends in one and is classified as something else is a typo in the
    /// table rather than a judgement call. Nothing is asserted about the codes these rules do not
    /// name — that is where judgement lives, and re-deriving it here would turn this file back into
    /// a copy of the rule it is meant to check.
    /// </remarks>
    [Fact]
    public void A_codes_name_never_contradicts_its_kind()
    {
        var unambiguous = new (string Suffix, ErrorType Type)[]
        {
            ("NotFound", ErrorType.NotFound),
            ("AlreadyExists", ErrorType.Conflict),
            ("Taken", ErrorType.Conflict),
            ("Unauthorized", ErrorType.Unauthorized),
        };

        var contradictions = ExpectedErrorStatuses.Kinds
            .SelectMany(entry =>
            {
                return unambiguous
                    .Where(rule =>
                    {
                        return entry.Key.EndsWith(rule.Suffix, StringComparison.Ordinal) && entry.Value != rule.Type;
                    })
                    .Select(rule => { return $"{entry.Key} is {entry.Value}, not {rule.Type}"; });
            })
            .ToList();

        Assert.True(contradictions.Count == 0, string.Join("; ", contradictions));
    }

    /// <summary>Every error code the shipped assemblies declare has a written-down kind.</summary>
    /// <remarks>
    /// This is the guard that stops the classification going blind: a code added to any module's
    /// resx would otherwise be typed at its one call site by whoever wrote it and never reviewed
    /// anywhere. Here it fails by name until it is classified.
    /// </remarks>
    [Fact]
    public void Every_shipped_error_code_is_classified()
    {
        var unclassified = ErrorCodeCensus.AllCodes()
            .Where(code => { return !ExpectedErrorStatuses.Kinds.ContainsKey(code); })
            .Where(code => { return !ExpectedErrorStatuses.AnsweredOutsideResultTranslation.ContainsKey(code); })
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            $"Error codes with no decided ErrorType: {string.Join(", ", unclassified)}. " +
            "Add each to ExpectedErrorStatuses.Kinds with the kind of failure it is.");
    }

    /// <summary>Every classified code still exists in the shipped assemblies.</summary>
    [Fact]
    public void Every_classified_code_still_exists()
    {
        var shipped = ErrorCodeCensus.AllCodes().ToHashSet(StringComparer.Ordinal);
        var stale = ExpectedErrorStatuses.Kinds.Keys
            .Concat(ExpectedErrorStatuses.AnsweredOutsideResultTranslation.Keys)
            .Where(code => { return !shipped.Contains(code); })
            .OrderBy(code => { return code; }, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0, $"Kinds decided for codes no assembly declares any more: {string.Join(", ", stale)}");
    }

    /// <summary>The census reads real codes out of the shipped assemblies rather than finding nothing.</summary>
    /// <remarks>
    /// The positive control. Assembly discovery is the axis that can silently stop matching — a
    /// renamed resource, a module that stops embedding its resx, a test run from an output folder
    /// that holds no module assemblies — and every assertion above passes trivially when the census
    /// is empty. So the census is required to have found several assemblies, a three-figure number
    /// of codes, and three specific codes planted here as the value the probe must find.
    /// </remarks>
    [Fact]
    public void Census_discovers_codes_from_every_shipping_assembly()
    {
        var byAssembly = ErrorCodeCensus.ByAssembly();
        var codes = ErrorCodeCensus.AllCodes();

        Assert.True(byAssembly.Count >= 10, $"Only {byAssembly.Count} assemblies declared error codes: {string.Join(", ", byAssembly.Keys)}");
        Assert.True(codes.Count >= 100, $"Census found only {codes.Count} error codes");
        Assert.Contains("SiteNotFound", codes);
        Assert.Contains("InvalidCredentialsUnauthorized", codes);
        Assert.Contains("AgentFirewallPortsMisconfigured", codes);
    }

    /// <summary>A successful result is still translated to 200, so the failure arms are not the only path exercised.</summary>
    [Fact]
    public void Success_is_translated_to_ok()
    {
        var result = Result<string>.Ok("value");

        var response = Assert.IsType<OkObjectResult>(result.ToActionResult(NewHttpContext()));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>Translates a failure through the real public surface.</summary>
    /// <param name="code">The machine-stable error code to translate.</param>
    /// <param name="type">The kind of failure it is.</param>
    /// <returns>The response the panel would send.</returns>
    private static ObjectResult Translate(string code, ErrorType type)
    {
        var result = Result<string>.Fail(Error.Of(code, type));
        return Assert.IsType<ObjectResult>(result.ToActionResult(NewHttpContext()));
    }

    /// <summary>Builds a request context with an empty container, the way an unloaded host presents one.</summary>
    /// <returns>The context handed to the extension under test.</returns>
    private static DefaultHttpContext NewHttpContext()
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
    }
}
