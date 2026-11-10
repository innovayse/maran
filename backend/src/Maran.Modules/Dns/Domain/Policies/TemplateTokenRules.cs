using System.Text;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The one statement of which variables a template record's value may name: the closed catalogue, how a token
/// is spelled, and where each variable is allowed. A brace that is not part of a well-formed token of the
/// catalogue is refused when the template is saved, so nothing reaches expansion that it would have to guess at.
/// </summary>
/// <remarks>
/// Only the VALUE of a record carries variables. An owner name, a tag and every number are literal: an owner
/// name with a brace is refused by <see cref="RecordNamePolicy"/> when the sample expansion is shape-checked.
/// There is no escape for a literal brace, because no value of any record type the panel stores needs one.
/// </remarks>
public static class TemplateTokenRules
{
    /// <summary>The character that opens a token.</summary>
    private const char Open = '{';

    /// <summary>The character that closes a token.</summary>
    private const char Close = '}';

    /// <summary>Every variable of the catalogue, in the order a screen lists them.</summary>
    public static IReadOnlyList<TemplateVariable> All { get; } = Enum.GetValues<TemplateVariable>();

    /// <summary>Spells a variable as a token, braces included: <c>{apex}</c>.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>The token.</returns>
    public static string TokenOf(TemplateVariable variable)
    {
        return Open + NameOf(variable) + Close;
    }

    /// <summary>Tells the variables a record type's value may name.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>
    /// A: the IPv4 address. AAAA: the IPv6 address. CNAME, MX, SRV and NS targets: the apex and the two name
    /// servers. TXT text: all six. CAA value: the apex.
    /// </returns>
    public static IReadOnlyList<TemplateVariable> AllowedIn(RecordType type)
    {
        return type switch
        {
            RecordType.A => [TemplateVariable.Ipv4],
            RecordType.Aaaa => [TemplateVariable.Ipv6],
            RecordType.Cname or RecordType.Mx or RecordType.Srv or RecordType.Ns =>
                [TemplateVariable.Apex, TemplateVariable.Ns1, TemplateVariable.Ns2],
            RecordType.Txt => All,
            RecordType.Caa => [TemplateVariable.Apex],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown record type."),
        };
    }

    /// <summary>Weighs the variables a record value names against the closed catalogue and the record's type.</summary>
    /// <param name="type">The record type.</param>
    /// <param name="value">The value as the administrator wrote it.</param>
    /// <returns>
    /// <see cref="TemplateTokenVerdict.Accepted"/> for a value with no variable or only allowed ones, else the
    /// first thing found wrong, in the order the value is read.
    /// </returns>
    public static TemplateTokenVerdict Check(RecordType type, string value)
    {
        var named = new List<TemplateVariable>();
        var verdict = Scan(value, named);
        if (verdict != TemplateTokenVerdict.Accepted)
        {
            return verdict;
        }

        var allowed = AllowedIn(type);

        return named.All(variable =>
        {
            return allowed.Contains(variable);
        }) ? TemplateTokenVerdict.Accepted : TemplateTokenVerdict.NotAllowedHere;
    }

    /// <summary>Lists the variables a value names, each once, in the order they first appear.</summary>
    /// <param name="value">A value that <see cref="Check"/> accepted.</param>
    /// <returns>The variables; empty for a literal value.</returns>
    /// <exception cref="ArgumentException">The value has a malformed or unknown token.</exception>
    public static IReadOnlyList<TemplateVariable> VariablesIn(string value)
    {
        var named = new List<TemplateVariable>();
        if (Scan(value, named) != TemplateTokenVerdict.Accepted)
        {
            throw new ArgumentException("A value is read for its variables only after the token rules accepted it.", nameof(value));
        }

        return named.Distinct().ToList();
    }

    /// <summary>Replaces every token of a value with what the context says it stands for, in one pass.</summary>
    /// <param name="value">A value that <see cref="Check"/> accepted.</param>
    /// <param name="context">The values of the variables.</param>
    /// <returns>The value with its tokens replaced; what was substituted is never read for tokens again.</returns>
    /// <exception cref="ArgumentException">The value has a malformed or unknown token.</exception>
    public static string Substitute(string value, TemplateContext context)
    {
        var named = new List<TemplateVariable>();
        if (Scan(value, named) != TemplateTokenVerdict.Accepted)
        {
            throw new ArgumentException("A value is expanded only after the token rules accepted it.", nameof(value));
        }

        var result = new StringBuilder(value.Length);
        var index = 0;
        var next = 0;
        while (index < value.Length)
        {
            if (value[index] != Open)
            {
                result.Append(value[index]);
                index++;
                continue;
            }

            var close = value.IndexOf(Close, index + 1);
            result.Append(context.ValueOf(named[next]));
            next++;
            index = close + 1;
        }

        return result.ToString();
    }

    /// <summary>Reads a value left to right, collecting the variables it names or stopping at the first fault.</summary>
    /// <param name="value">The value.</param>
    /// <param name="named">Receives the variables in order of appearance, repeats included.</param>
    /// <returns>The verdict on the braces and on the names inside them; allowed positions are not judged here.</returns>
    private static TemplateTokenVerdict Scan(string value, List<TemplateVariable> named)
    {
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index];
            if (character == Close)
            {
                return TemplateTokenVerdict.Unbalanced;
            }

            if (character != Open)
            {
                index++;
                continue;
            }

            var close = value.IndexOf(Close, index + 1);
            if (close < 0)
            {
                return TemplateTokenVerdict.Unbalanced;
            }

            var body = value.Substring(index + 1, close - index - 1);
            if (body.Contains(Open, StringComparison.Ordinal))
            {
                return TemplateTokenVerdict.Unbalanced;
            }

            var variable = Parse(body);
            if (variable is null)
            {
                return TemplateTokenVerdict.UnknownToken;
            }

            named.Add(variable.Value);
            index = close + 1;
        }

        return TemplateTokenVerdict.Accepted;
    }

    /// <summary>Reads a token's name as a variable, exactly and case-sensitively.</summary>
    /// <param name="name">The text between the braces.</param>
    /// <returns>The variable, or null when the catalogue has no such name.</returns>
    private static TemplateVariable? Parse(string name)
    {
        foreach (var variable in All)
        {
            if (string.Equals(NameOf(variable), name, StringComparison.Ordinal))
            {
                return variable;
            }
        }

        return null;
    }

    /// <summary>Spells a variable's name as it appears between the braces.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>The lower-case name.</returns>
    private static string NameOf(TemplateVariable variable)
    {
        return variable.ToString().ToLowerInvariant();
    }
}
