using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// Replays the billing contract against a host, step by step, and reports every difference between what the
/// contract says the provider relies on and what the panel answered.
/// </summary>
/// <remarks>
/// <para>
/// A step is checked for its status first and, when the status is the expected one, for its code, its
/// headers, the properties the provider reads (presence, type, value) and the properties it must not be
/// given. It checks only what the contract names, so an additive change passes and a removal or a retype
/// fails: the same direction as <c>maran openapi</c>.
/// </para>
/// <para>
/// A step the replay cannot run (an input, a credential or a host condition it was not given) is reported
/// as skipped and never as passed. The panel's own test asserts that nothing was skipped.
/// </para>
/// </remarks>
public static class BillingContractReplayer
{
    /// <summary>The header the key travels in.</summary>
    private const string KeyHeader = "X-Api-Key";

    /// <summary>The credential a step without one uses.</summary>
    private const string DefaultCredential = "full";

    /// <summary>The built-in credential that sends no key at all.</summary>
    private const string NoKey = "none";

    /// <summary>The built-in credential that sends a well-formed key the panel never issued.</summary>
    private const string UnknownKey = "unknown";

    /// <summary>A well-formed key (prefix of eight hex characters, 43 characters of secret) nobody was issued.</summary>
    private const string NeverIssued = "mk_0badc0de_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>The longest stretch of an answer quoted in a failure.</summary>
    private const int QuotedBodyLength = 300;

    /// <summary>Finds <c>{{name}}</c> placeholders.</summary>
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>Replays every step of <paramref name="contract"/> in order.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="environment">The client, inputs, credentials and arrangements the replay has.</param>
    /// <returns>What was sent, what was skipped and every difference found.</returns>
    public static async Task<BillingReplayReport> ReplayAsync(
        BillingContractFile contract,
        BillingReplayEnvironment environment)
    {
        var variables = new Dictionary<string, string>(environment.Inputs, StringComparer.Ordinal)
        {
            ["run"] = Guid.NewGuid().ToString("N"),
            ["suffix"] = Guid.NewGuid().ToString("N")[..8],
            ["randomGuid"] = Guid.NewGuid().ToString(),
        };
        var skipped = new List<string>();
        var failures = new List<string>();
        var replayed = 0;

        foreach (var interaction in contract.Interactions)
        {
            var arrangement = environment.Arrangements.SingleOrDefault(candidate =>
            {
                return candidate.Name == interaction.Arrange;
            });
            var unmet = UnmetNeed(interaction, environment, arrangement);
            if (unmet is not null)
            {
                skipped.Add($"{interaction.Name}: {unmet}");
                continue;
            }

            if (arrangement is not null)
            {
                await arrangement.ApplyAsync();
            }

            try
            {
                failures.AddRange(await SendAndCheckAsync(contract, interaction, environment, variables));
            }
            finally
            {
                if (arrangement is not null)
                {
                    await arrangement.RestoreAsync();
                }
            }

            replayed++;
        }

        return new BillingReplayReport(replayed, skipped, failures);
    }

    /// <summary>Says why a step cannot be sent, or <c>null</c> when it can.</summary>
    /// <param name="interaction">The step.</param>
    /// <param name="environment">What the replay has.</param>
    /// <param name="arrangement">The arrangement the step names, if the replay has it.</param>
    /// <returns>The reason, or <c>null</c>.</returns>
    private static string? UnmetNeed(
        BillingInteraction interaction,
        BillingReplayEnvironment environment,
        BillingArrangement? arrangement)
    {
        var credential = interaction.Credential ?? DefaultCredential;
        if (credential is not (NoKey or UnknownKey) && !environment.Credentials.ContainsKey(credential))
        {
            return $"no '{credential}' credential";
        }

        foreach (var input in interaction.Requires ?? [])
        {
            if (!environment.Inputs.ContainsKey(input))
            {
                return $"no '{input}' input";
            }
        }

        return interaction.Arrange is not null && arrangement is null
            ? $"cannot arrange '{interaction.Arrange}'"
            : null;
    }

    /// <summary>Sends one step and returns the differences found in the answer.</summary>
    /// <param name="contract">The contract, for its base path.</param>
    /// <param name="interaction">The step.</param>
    /// <param name="environment">What the replay has.</param>
    /// <param name="variables">Inputs, built-ins and captured values; the step's captures are added to it.</param>
    /// <returns>The differences, each naming the step; none when the answer is what the contract relies on.</returns>
    private static async Task<List<string>> SendAndCheckAsync(
        BillingContractFile contract,
        BillingInteraction interaction,
        BillingReplayEnvironment environment,
        Dictionary<string, string> variables)
    {
        var failures = new List<string>();
        HttpResponseMessage response;

        try
        {
            using var request = BuildRequest(contract, interaction, environment, variables);
            response = await environment.Client.SendAsync(request);
        }
        catch (InvalidOperationException problem)
        {
            failures.Add($"{interaction.Name}: the step could not be built: {problem.Message}");
            return failures;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var expected = interaction.Response;

            if ((int)response.StatusCode != expected.Status)
            {
                failures.Add($"{interaction.Name}: expected status {expected.Status} but the panel answered "
                    + $"{(int)response.StatusCode}: {Quote(body)}");
                return failures;
            }

            CheckEmpty(interaction, body, failures);
            CheckHeaders(interaction, response, variables, failures);
            CheckBody(interaction, body, variables, failures);

            return failures;
        }
    }

    /// <summary>Builds the HTTP request of a step, with every placeholder expanded and the credential attached.</summary>
    /// <param name="contract">The contract, for its base path.</param>
    /// <param name="interaction">The step.</param>
    /// <param name="environment">What the replay has.</param>
    /// <param name="variables">The values placeholders are replaced with.</param>
    /// <returns>The request, which the caller disposes.</returns>
    private static HttpRequestMessage BuildRequest(
        BillingContractFile contract,
        BillingInteraction interaction,
        BillingReplayEnvironment environment,
        Dictionary<string, string> variables)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(interaction.Request.Method),
            contract.BasePath + Expand(interaction.Request.Path, variables));

        foreach (var (name, value) in interaction.Request.Headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, Expand(value, variables));
        }

        var credential = interaction.Credential ?? DefaultCredential;
        if (credential != NoKey)
        {
            request.Headers.TryAddWithoutValidation(
                KeyHeader,
                credential == UnknownKey ? NeverIssued : environment.Credentials[credential]);
        }

        if (interaction.Request.Body is { } body)
        {
            request.Content = new StringContent(Expand(body.GetRawText(), variables), Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    /// <summary>Checks that an answer the contract says is empty is empty.</summary>
    /// <param name="interaction">The step.</param>
    /// <param name="body">The answer's body.</param>
    /// <param name="failures">Where a difference is added.</param>
    private static void CheckEmpty(BillingInteraction interaction, string body, List<string> failures)
    {
        if (interaction.Response.Empty && body.Length > 0)
        {
            failures.Add($"{interaction.Name}: expected an empty body but the panel answered {Quote(body)}");
        }
    }

    /// <summary>Checks the response headers the step names.</summary>
    /// <param name="interaction">The step.</param>
    /// <param name="response">The answer.</param>
    /// <param name="variables">The values placeholders are replaced with.</param>
    /// <param name="failures">Where a difference is added.</param>
    private static void CheckHeaders(
        BillingInteraction interaction,
        HttpResponseMessage response,
        Dictionary<string, string> variables,
        List<string> failures)
    {
        foreach (var (name, expected) in interaction.Response.Headers ?? new Dictionary<string, string>())
        {
            var wanted = Expand(expected, variables);
            if (!response.Headers.TryGetValues(name, out var values) || !values.Contains(wanted))
            {
                failures.Add($"{interaction.Name}: expected the response header {name}: {wanted} but it was "
                    + (values is null ? "absent" : string.Join(", ", values)));
            }
        }
    }

    /// <summary>
    /// Checks the code, the properties the provider reads, the properties it must not be given, and keeps the
    /// values the step captures.
    /// </summary>
    /// <param name="interaction">The step.</param>
    /// <param name="body">The answer's body.</param>
    /// <param name="variables">The values placeholders are replaced with; captures are added to it.</param>
    /// <param name="failures">Where a difference is added.</param>
    private static void CheckBody(
        BillingInteraction interaction,
        string body,
        Dictionary<string, string> variables,
        List<string> failures)
    {
        var expected = interaction.Response;
        var needsBody = expected.Code is not null
            || expected.Properties is { Count: > 0 }
            || expected.Absent is { Count: > 0 }
            || expected.Capture is { Count: > 0 };
        if (!needsBody)
        {
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            failures.Add($"{interaction.Name}: expected a JSON body but the panel answered {Quote(body)}");
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (expected.Code is not null)
            {
                CheckProperty(
                    interaction,
                    root,
                    new ResponseExpectation("code", "string", EqualsLiteral: expected.Code),
                    variables,
                    failures);
            }

            foreach (var property in expected.Properties ?? [])
            {
                CheckProperty(interaction, root, property, variables, failures);
            }

            foreach (var path in expected.Absent ?? [])
            {
                if (TryResolve(root, path, out _))
                {
                    failures.Add($"{interaction.Name}: the answer carries '{path}', which the provider must not be given");
                }
            }

            foreach (var (variable, path) in expected.Capture ?? new Dictionary<string, string>())
            {
                if (TryResolve(root, path, out var found))
                {
                    variables[variable] = TextOf(found);
                }
                else
                {
                    failures.Add($"{interaction.Name}: cannot capture '{variable}' because the answer has no '{path}'");
                }
            }
        }
    }

    /// <summary>Checks that one property exists, has the type the contract states and holds the value it states.</summary>
    /// <param name="interaction">The step.</param>
    /// <param name="root">The answer's body.</param>
    /// <param name="expectation">What the contract says about the property.</param>
    /// <param name="variables">The values placeholders are replaced with.</param>
    /// <param name="failures">Where a difference is added.</param>
    private static void CheckProperty(
        BillingInteraction interaction,
        JsonElement root,
        ResponseExpectation expectation,
        Dictionary<string, string> variables,
        List<string> failures)
    {
        if (!TryResolve(root, expectation.Name, out var found))
        {
            failures.Add($"{interaction.Name}: the answer has no '{expectation.Name}'");
            return;
        }

        if (!TypeMatches(found, expectation.Type))
        {
            failures.Add($"{interaction.Name}: '{expectation.Name}' should be a {expectation.Type} but is "
                + $"{found.ValueKind} {Quote(found.GetRawText())}");
            return;
        }

        string? wanted;
        try
        {
            wanted = expectation.EqualsLiteral is not null
                ? Expand(expectation.EqualsLiteral, variables)
                : expectation.EqualsVariable is not null ? Expand("{{" + expectation.EqualsVariable + "}}", variables) : null;
        }
        catch (InvalidOperationException problem)
        {
            failures.Add($"{interaction.Name}: '{expectation.Name}' cannot be compared: {problem.Message}");
            return;
        }

        if (wanted is not null && !string.Equals(TextOf(found), wanted, StringComparison.Ordinal))
        {
            failures.Add($"{interaction.Name}: '{expectation.Name}' should be {wanted} but is {TextOf(found)}");
        }
    }

    /// <summary>Finds a property by its dotted path; <c>$</c> is the body itself.</summary>
    /// <param name="root">The body.</param>
    /// <param name="path">The dotted path.</param>
    /// <param name="found">The property's value, when there is one.</param>
    /// <returns>Whether the property exists.</returns>
    private static bool TryResolve(JsonElement root, string path, out JsonElement found)
    {
        found = root;
        if (path == "$")
        {
            return true;
        }

        foreach (var segment in path.Split('.'))
        {
            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(segment, out var next))
            {
                return false;
            }

            found = next;
        }

        return true;
    }

    /// <summary>Whether a value has the type the contract names.</summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The type's name from the contract.</param>
    /// <returns>Whether the value is of that type; <c>false</c> for a name the replay does not know.</returns>
    private static bool TypeMatches(JsonElement value, string type)
    {
        return type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "guid" => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            "instant" => value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => false,
        };
    }

    /// <summary>A value as text: a string as itself, anything else as its JSON.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    private static string TextOf(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    /// <summary>Replaces every <c>{{name}}</c> with its value.</summary>
    /// <param name="text">The text holding placeholders.</param>
    /// <param name="variables">The values.</param>
    /// <returns>The text with the placeholders replaced.</returns>
    /// <exception cref="InvalidOperationException">A placeholder names no input, built-in or captured value.</exception>
    private static string Expand(string text, Dictionary<string, string> variables)
    {
        return Placeholder.Replace(text, match =>
        {
            return variables.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"'{{{{{match.Groups[1].Value}}}}}' is not an input, a built-in or a value an earlier step captured");
        });
    }

    /// <summary>Shortens an answer for a failure message.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text, cut to a readable length.</returns>
    private static string Quote(string text)
    {
        return text.Length <= QuotedBodyLength ? text : text[..QuotedBodyLength] + "...";
    }
}
