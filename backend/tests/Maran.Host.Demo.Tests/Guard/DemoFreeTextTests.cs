using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Exceptions;
using Maran.SharedKernel.Security;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The walk finds text in a bound command wherever it sits and skips what is shown to no one.</summary>
public sealed class DemoFreeTextTests
{
    /// <summary>A record inside a record, as a command holds a sub-object.</summary>
    /// <param name="Label">A text member.</param>
    private sealed record Inner(string Label);

    /// <summary>A command shaped like the panel's: texts, a list, a dictionary, a secret and a number.</summary>
    /// <param name="Name">A text member.</param>
    /// <param name="Inner">A nested record.</param>
    /// <param name="Tags">A list of texts.</param>
    /// <param name="Rows">A list of records.</param>
    /// <param name="Environment">A dictionary of texts.</param>
    /// <param name="Password">A secret the walk must not read.</param>
    /// <param name="Secret">A SensitiveString the walk must not read.</param>
    /// <param name="Count">A number.</param>
    private sealed record Sample(
        string? Name,
        Inner? Inner,
        IReadOnlyList<string>? Tags,
        IReadOnlyList<Inner>? Rows,
        IReadOnlyDictionary<string, string>? Environment,
        string? Password,
        SensitiveString? Secret,
        int Count);

    /// <summary>A command with credentials and labels that merely mention them.</summary>
    /// <param name="Password">A credential.</param>
    /// <param name="Token">A credential.</param>
    /// <param name="TokenName">A label the next visitor sees.</param>
    /// <param name="SecretNote">A note the next visitor sees.</param>
    private sealed record Labelled(string Password, string Token, string TokenName, string SecretNote);

    /// <summary>A node one level of a chain of any length.</summary>
    /// <param name="Next">The next node.</param>
    /// <param name="Text">A text at this level.</param>
    private sealed record Level(Level? Next, string Text);

    /// <summary>A type whose getter throws.</summary>
    private sealed class Booby
    {
        /// <summary>What the broken getter says.</summary>
        private readonly string _failure = "broken getter";

        /// <summary>A member that cannot be read, as a getter that fails.</summary>
        public string Text
        {
            get
            {
                throw new InvalidOperationException(_failure);
            }
        }
    }

    /// <summary>A node that points back at itself.</summary>
    private sealed class Loop
    {
        /// <summary>The same node again.</summary>
        public Loop? Next { get; set; }

        /// <summary>A text.</summary>
        public string Text { get; set; } = "kept";
    }

    /// <summary>Reads the texts of a set of named arguments.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>Field and text pairs.</returns>
    private static List<(string Field, string Text)> Walk(params (string Name, object? Value)[] arguments)
    {
        return DemoFreeText.Of(arguments.Select(a => { return new KeyValuePair<string, object?>(a.Name, a.Value); }))
            .ToList();
    }

    /// <summary>Every text in a command is found, with the dotted name of the member holding it.</summary>
    [Fact]
    public void Text_is_found_in_members_lists_records_and_dictionaries()
    {
        var found = Walk((
            "command",
            new Sample(
                "n",
                new Inner("i"),
                ["t1", "t2"],
                [new Inner("r1")],
                new Dictionary<string, string> { ["K"] = "V" },
                null,
                null,
                3)));

        Assert.Equal(
            [
                "command.Name=n",
                "command.Inner.Label=i",
                "command.Tags=t1",
                "command.Tags=t2",
                "command.Rows.Label=r1",
                "command.Environment=K",
                "command.Environment=V",
            ],
            found.Select(f => { return $"{f.Field}={f.Text}"; }));
    }

    /// <summary>A bare string argument, such as a route value, is a text.</summary>
    [Fact]
    public void A_string_argument_is_a_text()
    {
        Assert.Equal([("name", "x")], Walk(("name", "x")));
    }

    /// <summary>The control for the next test: the same words in a plain member ARE found.</summary>
    [Fact]
    public void The_same_text_in_an_ordinary_member_is_found()
    {
        var found = Walk(("c", new Sample("casino", null, null, null, null, null, null, 0)));

        Assert.Equal(["casino"], found.Select(f => { return f.Text; }));
    }

    /// <summary>A password, a SensitiveString, a number and a null are not texts to screen.</summary>
    [Fact]
    public void Secrets_numbers_and_nulls_are_not_read()
    {
        var found = Walk(("c", new Sample(null, null, null, null, null, "casino", new SensitiveString("casino"), 7)));

        Assert.Empty(found);
    }

    /// <summary>A member whose name says it is a secret is skipped even when it holds a plain string.</summary>
    [Fact]
    public void An_argument_named_for_a_secret_is_skipped()
    {
        Assert.Empty(Walk(("newPassword", "casino")));
        Assert.Empty(Walk(("refreshToken", "casino")));
    }

    /// <summary>
    /// The inverse control: only a name that IS a secret is skipped, so a label that merely mentions one is a text
    /// the next visitor sees and is screened.
    /// </summary>
    [Fact]
    public void A_name_that_only_contains_a_secret_word_is_screened()
    {
        Assert.Equal([("apiTokenLabel", "casino")], Walk(("apiTokenLabel", "casino")));
        Assert.Equal([("tokenName", "casino")], Walk(("tokenName", "casino")));

        var found = Walk(("c", new Labelled("casino", "casino", "casino", "casino")));

        Assert.Equal(["c.TokenName", "c.SecretNote"], found.Select(f => { return f.Field; }));
    }

    /// <summary>Every exempt name is exact: the list is what the walk skips, and nothing else.</summary>
    [Fact]
    public void The_exempt_names_are_the_exact_credential_names()
    {
        Assert.All(DemoFreeText.SecretNames, name => { Assert.Empty(Walk((name, "casino"))); });
        Assert.All(DemoFreeText.SecretNames, name => { Assert.Empty(Walk((name.ToUpperInvariant(), "casino"))); });
        Assert.NotEmpty(DemoFreeText.SecretNames);
    }

    /// <summary>An uploaded file contributes its name, and its bytes are never read.</summary>
    [Fact]
    public void An_uploaded_file_contributes_its_name_only()
    {
        var file = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("casino")), 0, 6, "file", "holiday.txt");

        Assert.Equal([("file", "holiday.txt")], Walk(("file", file)));
        Assert.Empty(Walk(("bytes", Encoding.UTF8.GetBytes("casino"))));
        Assert.Empty(Walk(("stream", new MemoryStream(Encoding.UTF8.GetBytes("casino")))));
    }

    /// <summary>A url is its original text.</summary>
    [Fact]
    public void A_uri_is_read_as_its_original_text()
    {
        Assert.Equal([("u", "https://evil.com/")], Walk(("u", new Uri("https://evil.com/"))));
    }

    /// <summary>An object that points back at itself ends the walk instead of looping, and its text is read once.</summary>
    [Fact]
    public void A_cycle_is_bounded()
    {
        var node = new Loop();
        node.Next = node;

        var found = Walk(("loop", node));

        Assert.Equal([("loop.Text", "kept")], found);
    }

    /// <summary>The same object reached twice contributes its text once, and two equal copies each contribute.</summary>
    [Fact]
    public void A_repeated_object_is_read_once_and_equal_copies_are_each_read()
    {
        var shared = new Inner("shared");

        Assert.Equal(["shared"], Walk(("c", new[] { shared, shared })).Select(f => { return f.Text; }));
        Assert.Equal(
            ["shared", "shared"],
            Walk(("c", new[] { new Inner("shared"), new Inner("shared") })).Select(f => { return f.Text; }));
    }

    /// <summary>Builds a chain of levels.</summary>
    /// <param name="levels">How many levels.</param>
    /// <returns>The top level.</returns>
    private static Level Chain(int levels)
    {
        Level? chain = null;
        for (var level = 0; level < levels; level++)
        {
            chain = new Level(chain, "kept");
        }

        return chain!;
    }

    /// <summary>Wraps a value in lists, one per level.</summary>
    /// <param name="levels">How many lists.</param>
    /// <returns>The outermost list.</returns>
    private static object NestedLists(int levels)
    {
        object inner = "casino";
        for (var level = 0; level < levels; level++)
        {
            inner = new List<object> { inner };
        }

        return inner;
    }

    /// <summary>The walk refuses at the depth cap instead of ending quietly there.</summary>
    [Fact]
    public void A_value_below_the_depth_cap_raises_instead_of_being_skipped()
    {
        var failure = Assert.Throws<DemoFreeTextUnreadableException>(() => { return Walk(("c", Chain(DemoFreeText.MaximumDepth + 3))); });
        Assert.StartsWith("c.Next", failure.Field, StringComparison.Ordinal);

        Assert.Throws<DemoFreeTextUnreadableException>(() => { return Walk(("c", NestedLists(DemoFreeText.MaximumDepth + 3))); });
    }

    /// <summary>The accepting control: at and below the cap every text is read.</summary>
    [Fact]
    public void Values_within_the_depth_cap_are_all_read()
    {
        Assert.Equal(DemoFreeText.MaximumDepth - 2, Walk(("c", Chain(DemoFreeText.MaximumDepth - 2))).Count);
        Assert.Equal(["casino"], Walk(("c", NestedLists(DemoFreeText.MaximumDepth - 1))).Select(f => { return f.Text; }));
    }

    /// <summary>A graph with more values than the budget raises, and one within it is read.</summary>
    [Fact]
    public void A_graph_larger_than_the_node_budget_raises_instead_of_being_read_in_part()
    {
        var tooMany = Enumerable.Range(0, DemoFreeText.MaximumNodes + 10).Select(i => { return i.ToString(CultureInfo.InvariantCulture); }).ToList();
        var fine = tooMany.Take(1000).ToList();

        Assert.Throws<DemoFreeTextUnreadableException>(() => { return Walk(("c", tooMany)); });
        Assert.Equal(1000, Walk(("c", fine)).Count);
    }

    /// <summary>Only the panel's own types are reflected into; a framework struct that holds text is not.</summary>
    [Fact]
    public void A_type_outside_the_panels_namespace_is_not_reflected_into()
    {
        Assert.Empty(Walk(("path", new PathString("/casino"))));
        Assert.Equal(["casino"], Walk(("inner", new Inner("casino"))).Select(f => { return f.Text; }));
    }

    /// <summary>A getter that throws is a refusal, not a server error, and the control reads fine.</summary>
    [Fact]
    public void A_getter_that_throws_raises_the_typed_refusal()
    {
        var failure = Assert.Throws<DemoFreeTextUnreadableException>(() => { return Walk(("c", new Booby())); });

        Assert.Equal("c.Text", failure.Field);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    /// <summary>Text inside a JsonElement is found: strings, property names and nested arrays; numbers are not text.</summary>
    [Fact]
    public void Text_inside_a_json_element_is_found()
    {
        using var document = JsonDocument.Parse("{\"note\":[\"casino\",{\"k\":\"v\"}],\"n\":1,\"t\":true}");

        var texts = Walk(("body", document.RootElement)).Select(f => { return f.Text; }).ToList();

        Assert.Equal(["note", "casino", "k", "v", "n", "t"], texts);
    }

    /// <summary>Text inside a JsonNode is found the same way, and a bare JSON string value is a text.</summary>
    [Fact]
    public void Text_inside_a_json_node_is_found()
    {
        var node = JsonNode.Parse("{\"note\":[\"casino\",{\"k\":\"v\"}],\"n\":1}")!;

        var texts = Walk(("body", node)).Select(f => { return f.Text; }).ToList();

        Assert.Contains("casino", texts);
        Assert.Contains("v", texts);
        Assert.Contains("k", texts);
        Assert.DoesNotContain("1", texts);
        Assert.Equal(["casino"], Walk(("body", JsonValue.Create("casino"))).Select(f => { return f.Text; }));
    }

    /// <summary>A JSON payload nested past the cap raises, like any other deep value; the control within it does not.</summary>
    [Fact]
    public void A_json_body_nested_past_the_cap_raises()
    {
        var deep = new string('[', DemoFreeText.MaximumDepth + 3) + "\"casino\"" + new string(']', DemoFreeText.MaximumDepth + 3);
        var shallow = new string('[', 3) + "\"casino\"" + new string(']', 3);
        using var tooDeep = JsonDocument.Parse(deep);
        using var fine = JsonDocument.Parse(shallow);

        Assert.Throws<DemoFreeTextUnreadableException>(() => { return Walk(("body", tooDeep.RootElement)); });
        Assert.Equal(["casino"], Walk(("body", fine.RootElement)).Select(f => { return f.Text; }));
    }

    /// <summary>The depth read from a type alone matches what the walk does, and a recursive type has no bound.</summary>
    [Fact]
    public void The_depth_of_a_type_is_read_from_the_type_alone()
    {
        Assert.Equal(3, DemoFreeText.DeepestTextOf(typeof(Sample)));
        Assert.Equal(1, DemoFreeText.DeepestTextOf(typeof(Inner)));
        Assert.Equal(0, DemoFreeText.DeepestTextOf(typeof(string)));
        Assert.Equal(-1, DemoFreeText.DeepestTextOf(typeof(int)));
        Assert.Equal(-1, DemoFreeText.DeepestTextOf(typeof(PathString)));
        Assert.Equal(2, DemoFreeText.DeepestTextOf(typeof(List<List<string>>)));
        Assert.Equal(int.MaxValue, DemoFreeText.DeepestTextOf(typeof(Level)));
        Assert.True(DemoFreeText.DeepestTextOf(typeof(List<List<List<List<List<List<List<List<List<string>>>>>>>>>))
            > DemoFreeText.MaximumDepth);
    }
}
