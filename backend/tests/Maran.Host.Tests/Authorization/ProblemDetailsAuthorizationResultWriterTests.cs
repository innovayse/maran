using System.Globalization;
using System.Text.Json;
using Maran.Host.Authorization;
using Maran.Host.Extensions;
using Maran.Modules.Identity.Authorization;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Authorization;

/// <summary>
/// Behavioural contract of <see cref="ProblemDetailsAuthorizationResultWriter"/>: a policy refusal
/// is a localized RFC 7807 body, and every other result is left to the framework.
/// </summary>
public sealed class ProblemDetailsAuthorizationResultWriterTests
{
    /// <summary>A role refusal is a problem body with the generic forbidden code.</summary>
    [Fact]
    public async Task A_role_refusal_is_a_problem_body_with_the_generic_forbidden_code()
    {
        using var body = await RefuseAsync("en", new RolesAuthorizationRequirement(["Admin"]));

        Assert.Equal("HostForbidden", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("You do not have access to this page.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("abc-123", body.RootElement.GetProperty(CorrelationIdKeys.PayloadField).GetString());
    }

    /// <summary>The refusal body names no role, policy or requirement.</summary>
    [Fact]
    public async Task The_refusal_body_names_no_role_policy_or_requirement()
    {
        using var body = await RefuseAsync("en", new RolesAuthorizationRequirement(["Admin"]));
        var raw = body.RootElement.GetRawText();

        Assert.DoesNotContain("Admin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Requirement", raw, StringComparison.Ordinal);
    }

    /// <summary>An unfinished two-factor enrolment gets its own code even when the role also failed.</summary>
    [Fact]
    public async Task An_unfinished_two_factor_enrolment_gets_its_own_code_even_when_the_role_also_failed()
    {
        using var body = await RefuseAsync(
            "en",
            new RolesAuthorizationRequirement(["Admin"]),
            new TwoFactorEnrolmentCompleteRequirement());

        Assert.Equal("HostTwoFactorSetupRequired", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>The refusal is written in Russian and Armenian for those request cultures.</summary>
    [Theory]
    [InlineData("ru", "У вас нет доступа к этой странице.")]
    [InlineData("hy", "Դուք այս էջի հասանելիություն չունեք։")]
    public async Task The_refusal_is_written_in_the_request_culture(string language, string expected)
    {
        using var body = await RefuseAsync(language, new RolesAuthorizationRequirement(["Admin"]));

        Assert.Equal(expected, body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>A successful result continues the pipeline and writes nothing.</summary>
    [Fact]
    public async Task A_successful_result_continues_the_pipeline_and_writes_nothing()
    {
        var context = CreateContext();
        var continued = false;

        await new ProblemDetailsAuthorizationResultWriter().HandleAsync(
            _ =>
            {
                continued = true;
                return Task.CompletedTask;
            },
            context,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Success());

        Assert.True(continued);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    /// <summary>Runs the handler on a forbidden result in <paramref name="language"/> and parses the body.</summary>
    /// <param name="language">The request culture.</param>
    /// <param name="failed">The requirements reported as failed.</param>
    private static async Task<JsonDocument> RefuseAsync(string language, params IAuthorizationRequirement[] failed)
    {
        var context = CreateContext();
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);
            await new ProblemDetailsAuthorizationResultWriter().HandleAsync(
                _ =>
                {
                    throw new InvalidOperationException("A refused request must not continue.");
                },
                context,
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
                PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed(failed)));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    /// <summary>Builds a request context whose services resolve the Host's own error texts.</summary>
    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection()
            .AddPanelLocalization()
            .AddSingleton<ILogger<ResxErrorTextProvider>>(NullLogger<ResxErrorTextProvider>.Instance)
            .AddSingleton<IErrorTextProvider, ResxErrorTextProvider>();

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        context.Items[CorrelationIdKeys.ItemsKey] = "abc-123";
        return context;
    }
}
