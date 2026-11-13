using System.Net;
using System.Reflection;
using System.Text.Json;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Services;
using Maran.Host.Demo.Tests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>
/// A write whose text is refused is answered 400 and never reaches the action; a clean one, a call the table
/// does not screen and a call the table does not name all run.
/// </summary>
public sealed class DemoContentFilterTests
{
    /// <summary>A command with one text member, as the panel's commands have.</summary>
    /// <param name="Name">The text a visitor typed.</param>
    private sealed record NamedCommand(string Name);

    /// <summary>A command holding many lines of text, as a pasted file does.</summary>
    /// <param name="Rows">The lines.</param>
    private sealed record TextLines(IReadOnlyList<string> Rows);

    /// <summary>A command nested through one member per level, to be built deeper than the walk reads.</summary>
    /// <param name="Next">The next level, or <c>null</c> at the bottom.</param>
    /// <param name="Text">A text at this level.</param>
    private sealed record Level(Level? Next, string Text);

    /// <summary>The result of running the filter once.</summary>
    /// <param name="ActionRan">Whether the action behind the filter ran.</param>
    /// <param name="Status">The status the filter wrote, or 200 when it wrote none.</param>
    /// <param name="Body">The JSON body the filter wrote, or <c>null</c> when it wrote none.</param>
    /// <param name="Audit">The entries the journal received.</param>
    private sealed record Outcome(bool ActionRan, int Status, JsonElement? Body, IReadOnlyList<AuditEntry> Audit);

    /// <summary>Runs the filter over the planted table.</summary>
    /// <param name="action">The planted controller's action reached; <c>null</c> for a non-controller one.</param>
    /// <param name="text">The text in the command's only member.</param>
    /// <param name="auditedRefusals">How many refusals the journal may still write.</param>
    /// <param name="command">The bound command; <c>null</c> binds a command holding <paramref name="text"/>.</param>
    /// <returns>What happened.</returns>
    private static async Task<Outcome> RunAsync(
        string? action,
        string text,
        int auditedRefusals = 200,
        object? command = null)
    {
        var audit = new RecordingAuditWriter();
        var options = new OptionsWrapper<DemoOptions>(
            new DemoOptions { AuditedRefusalsPerWindow = auditedRefusals });
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider(),
        };
        http.Request.Method = "POST";
        http.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
        http.Response.Body = new MemoryStream();
        ActionDescriptor descriptor = action is null
            ? new ActionDescriptor()
            : new ControllerActionDescriptor
            {
                MethodInfo = typeof(PlantedDemoController).GetMethod(action)!,
                ControllerTypeInfo = typeof(PlantedDemoController).GetTypeInfo(),
            };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { ["command"] = command ?? new NamedCommand(text) },
            controller: new object());
        var ran = false;
        var filter = new DemoContentFilter(
            DemoEndpoints.PlantedPolicy,
            DemoTextScreen.Default,
            new DemoAuditAllowance(options),
            new DemoAuditJournal(audit, new AdministratorUser()));

        await filter.OnActionExecutionAsync(
            context,
            () =>
            {
                ran = true;
                return Task.FromResult(
                    new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            });

        http.Response.Body.Position = 0;
        JsonElement? body = null;
        if (http.Response.Body.Length > 0)
        {
            using var document = await JsonDocument.ParseAsync(http.Response.Body);
            body = document.RootElement.Clone();
        }

        return new Outcome(ran, http.Response.StatusCode, body, audit.Entries);
    }

    /// <summary>The refusing control: blocked text is answered 400 and never reaches the action.</summary>
    [Fact]
    public async Task A_blocked_term_in_a_screened_action_is_refused_before_the_action_runs()
    {
        var outcome = await RunAsync(nameof(PlantedDemoController.CountedAsync), "Best casino ever");

        Assert.False(outcome.ActionRan);
        Assert.Equal((int)HttpStatusCode.BadRequest, outcome.Status);
        Assert.Equal("DemoContentRefused", outcome.Body!.Value.GetProperty("code").GetString());
        Assert.Equal("Name", outcome.Body.Value.GetProperty("content").GetProperty("field").GetString());
    }

    /// <summary>The refusal never carries the text back, and neither does the journal.</summary>
    [Fact]
    public async Task The_refusal_and_the_journal_do_not_repeat_the_text()
    {
        var outcome = await RunAsync(nameof(PlantedDemoController.OpenAsync), "Best casino ever");

        Assert.DoesNotContain("casino", outcome.Body!.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(outcome.Audit);
        Assert.Equal(DemoAuditJournal.ContentRefused, entry.Action);
        Assert.DoesNotContain("casino", entry.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A link off the allow-list is refused the same way.</summary>
    [Fact]
    public async Task A_link_off_the_allow_list_is_refused()
    {
        var outcome = await RunAsync(nameof(PlantedDemoController.CountedAsync), "see http://evil.com/");

        Assert.False(outcome.ActionRan);
        Assert.Equal((int)HttpStatusCode.BadRequest, outcome.Status);
    }

    /// <summary>Builds a command of lines that add up to a number of characters.</summary>
    /// <param name="characters">The total length.</param>
    /// <returns>The command, in lines of one thousand characters at most.</returns>
    private static TextLines LinesOf(int characters)
    {
        var lines = new List<string>();
        for (var left = characters; left > 0; left -= 1000)
        {
            lines.Add(new string('a', Math.Min(left, 1000)));
        }

        return new TextLines(lines);
    }

    /// <summary>
    /// More text than one request may put through the screen is refused whole, however clean it is: the screen's
    /// time is bounded by the request, not by what the visitor chooses to send.
    /// </summary>
    [Fact]
    public async Task A_request_with_more_text_than_the_budget_is_refused_even_when_it_is_clean()
    {
        var outcome = await RunAsync(
            nameof(PlantedDemoController.CountedAsync),
            string.Empty,
            command: LinesOf(DemoContentFilter.MaximumScreenedCharacters + 1));

        Assert.False(outcome.ActionRan);
        Assert.Equal((int)HttpStatusCode.BadRequest, outcome.Status);
    }

    /// <summary>The accepting control of the budget: text exactly at the budget is judged, and being clean it runs.</summary>
    [Fact]
    public async Task A_request_with_text_exactly_at_the_budget_runs_when_it_is_clean()
    {
        var outcome = await RunAsync(
            nameof(PlantedDemoController.CountedAsync),
            string.Empty,
            command: LinesOf(DemoContentFilter.MaximumScreenedCharacters));

        Assert.True(outcome.ActionRan);
    }

    /// <summary>The accepting control: clean text runs the action and writes nothing.</summary>
    [Fact]
    public async Task Clean_text_runs_the_action_and_writes_nothing()
    {
        var outcome = await RunAsync(nameof(PlantedDemoController.CountedAsync), "A blog, see https://example.com/");

        Assert.True(outcome.ActionRan);
        Assert.Equal(200, outcome.Status);
        Assert.Null(outcome.Body);
        Assert.Empty(outcome.Audit);
    }

    /// <summary>The table decides what is screened: a refused action and a sign-in are not.</summary>
    [Theory]
    [InlineData(nameof(PlantedDemoController.RefusedAsync))]
    [InlineData(nameof(PlantedDemoController.SignInAsync))]
    public async Task An_action_the_table_does_not_screen_runs_with_the_same_text(string action)
    {
        var outcome = await RunAsync(action, "Best casino ever");

        Assert.True(outcome.ActionRan);
        Assert.Empty(outcome.Audit);
    }

    /// <summary>An unnamed action and a non-controller one are left to the guard.</summary>
    [Theory]
    [InlineData(nameof(PlantedDemoController.UnnamedAsync))]
    [InlineData(null)]
    public async Task An_unnamed_or_non_controller_action_is_not_judged_here(string? action)
    {
        var outcome = await RunAsync(action, "Best casino ever");

        Assert.True(outcome.ActionRan);
    }

    /// <summary>With the journal's allowance spent the refusal is still answered, not journaled.</summary>
    [Fact]
    public async Task A_spent_audit_allowance_still_refuses_but_writes_nothing()
    {
        var outcome = await RunAsync(nameof(PlantedDemoController.CountedAsync), "casino", auditedRefusals: 0);

        Assert.False(outcome.ActionRan);
        Assert.Equal((int)HttpStatusCode.BadRequest, outcome.Status);
        Assert.Empty(outcome.Audit);
    }

    /// <summary>Builds a chain of levels, each holding a clean text.</summary>
    /// <param name="levels">How many levels.</param>
    /// <returns>The top level.</returns>
    private static Level Chain(int levels)
    {
        Level? chain = null;
        for (var level = 0; level < levels; level++)
        {
            chain = new Level(chain, "clean");
        }

        return chain!;
    }

    /// <summary>
    /// A command nested past the walk's depth is refused, not passed: text below the cap would be stored unscreened.
    /// </summary>
    [Fact]
    public async Task A_command_nested_deeper_than_the_walk_reads_is_refused()
    {
        var outcome = await RunAsync(
            nameof(PlantedDemoController.CountedAsync),
            "unused",
            command: Chain(DemoFreeText.MaximumDepth + 4));

        Assert.False(outcome.ActionRan);
        Assert.Equal((int)HttpStatusCode.BadRequest, outcome.Status);
        Assert.Equal("DemoContentRefused", outcome.Body!.Value.GetProperty("code").GetString());
    }

    /// <summary>The accepting control: the same shape within the cap runs the action.</summary>
    [Fact]
    public async Task A_command_nested_within_the_walk_runs_the_action()
    {
        var outcome = await RunAsync(
            nameof(PlantedDemoController.CountedAsync),
            "unused",
            command: Chain(DemoFreeText.MaximumDepth - 2));

        Assert.True(outcome.ActionRan);
        Assert.Empty(outcome.Audit);
    }
}
