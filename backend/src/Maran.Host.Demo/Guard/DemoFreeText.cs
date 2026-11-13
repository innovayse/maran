using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maran.Host.Demo.Guard.Exceptions;
using Maran.SharedKernel.Security;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Finds the text a visitor typed inside the arguments an action was bound to: every string, however deep it
/// sits in a command, a list of records, a dictionary or a JSON body, with the name of the member that holds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it walks and does not list fields.</b> The demo publishes whatever a visitor writes, wherever the
/// command keeps it, and a list of field names per endpoint is a second copy of every command that nobody
/// updates when a field is added. The walk reads the bound object, so a new text member is screened the day
/// it is added.
/// </para>
/// <para>
/// <b>What it reads.</b> Properties of the panel's own types only (namespace <c>Maran</c>), of key/value pairs,
/// and the contents of lists, dictionaries, <see cref="JsonElement"/> and <see cref="JsonNode"/>: an allow-list,
/// so a framework or third-party object that happens to sit in a command is never reflected into.
/// </para>
/// <para>
/// <b>What it leaves out on purpose.</b> A member whose name is exactly one of <see cref="SecretNames"/>
/// (compared without regard to case) and any <see cref="SensitiveString"/>: that text is a credential, is
/// never shown to another visitor, and screening it would only refuse a password for what it spells. The match
/// is on the whole name, not a fragment: a field called <c>TokenName</c> or <c>SecretNote</c> is a label other
/// visitors see and is screened. Bytes and streams are not text; an uploaded file contributes its name, not
/// its content.
/// </para>
/// <para>
/// <b>It refuses, it does not skip.</b> A walk that stopped quietly at a limit would store the text beyond it
/// unscreened, so reaching <see cref="MaximumDepth"/> or the node budget, a getter that throws, or a repeat
/// of an object already read (a cycle, which has no new text) are handled by their own rule: the first three
/// raise <see cref="DemoFreeTextUnreadableException"/> and the write is refused; a repeat is read once.
/// <see cref="DeepestTextOf"/> gives the same depth from a type alone, so a test can hold every command to the cap.
/// </para>
/// </remarks>
public static class DemoFreeText
{
    /// <summary>How many levels deep the walk goes: each property and each list or dictionary level is one.</summary>
    public const int MaximumDepth = 8;

    /// <summary>How many values one set of arguments may hold before the walk gives up and refuses.</summary>
    public const int MaximumNodes = 100_000;

    /// <summary>
    /// The exact member names that hold a credential, compared without regard to case. Every entry is a name some
    /// request of the panel really carries; a new credential field is added here, and nothing else is exempt.
    /// </summary>
    public static readonly FrozenSet<string> SecretNames = new[]
    {
        "Password", "CurrentPassword", "NewPassword", "Passphrase", "Token", "AccessToken", "RefreshToken",
        "SetupToken", "Secret", "TotpSecret", "PrivateKey", "PrivateKeyPem",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>How the walk and the depth census treat a value of one type: the single table both read.</summary>
    private enum ValueKind
    {
        /// <summary>A string, a URI or an uploaded file's name: text, read as it is.</summary>
        Text,

        /// <summary>A JSON element or node: text, whose own structure the walk reads and bounds at run time.</summary>
        Json,

        /// <summary>A credential, bytes, a stream, a number or an enum: never text.</summary>
        Skipped,

        /// <summary>Anything else: a list, a dictionary or an object whose members may hold text.</summary>
        Other,
    }

    /// <summary>What one walk has used: the values it may still read and the objects it has already read.</summary>
    private sealed class WalkState
    {
        /// <summary>The values the walk may still read.</summary>
        public int Remaining { get; set; } = MaximumNodes;

        /// <summary>The objects already read, compared by reference.</summary>
        public HashSet<object> Visited { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Counts one value read at a depth.</summary>
        /// <param name="name">The dotted name of the member the value was read from.</param>
        /// <param name="depth">How deep the walk is.</param>
        /// <exception cref="DemoFreeTextUnreadableException">The depth or the budget is exceeded.</exception>
        public void Enter(string name, int depth)
        {
            if (depth > MaximumDepth)
            {
                throw new DemoFreeTextUnreadableException(name, $"nested deeper than {MaximumDepth} levels");
            }

            Remaining--;
            if (Remaining < 0)
            {
                throw new DemoFreeTextUnreadableException(name, $"more than {MaximumNodes} values");
            }
        }
    }

    /// <summary>Every piece of text in the arguments.</summary>
    /// <param name="arguments">The bound arguments of the action, by parameter name.</param>
    /// <returns>Each text with the dotted name of the member that holds it.</returns>
    /// <exception cref="DemoFreeTextUnreadableException">Raised while enumerating, when the arguments cannot be read in full.</exception>
    public static IEnumerable<(string Field, string Text)> Of(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return Read(arguments);
    }

    /// <summary>The enumeration behind <see cref="Of"/>, split off so its argument is checked at the call.</summary>
    /// <param name="arguments">The bound arguments of the action, by parameter name.</param>
    /// <returns>Each text with the dotted name of the member that holds it.</returns>
    private static IEnumerable<(string Field, string Text)> Read(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        var state = new WalkState();
        foreach (var argument in arguments)
        {
            if (SecretNames.Contains(argument.Key))
            {
                continue;
            }

            foreach (var found in Walk(argument.Key, argument.Value, 0, state))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// The depth at which the deepest string sits when a value of this type is walked, read from the type alone
    /// the way <see cref="Of"/> reads a value: a property or a list or dictionary level is one step.
    /// </summary>
    /// <param name="type">The declared type of an argument.</param>
    /// <returns>
    /// The deepest depth of a string, <c>-1</c> when the type holds none, and <see cref="int.MaxValue"/> when the
    /// type contains itself, so no depth bounds it.
    /// </returns>
    public static int DeepestTextOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return DeepestText(type, 0, []);
    }

    /// <summary>
    /// Whether an object's public members are worth reading: the panel's own records and commands are, and so is
    /// a key/value pair (the element of a dictionary-shaped list); numbers, enums, dates, ids and every framework
    /// or third-party type are not text.
    /// </summary>
    /// <param name="type">The runtime type of the value.</param>
    /// <returns><c>true</c> when its properties may hold text.</returns>
    private static bool HoldsMembersWorthReading(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return true;
        }

        return !type.IsPrimitive
            && !type.IsEnum
            && type.Namespace is { } space
            && (space == "Maran" || space.StartsWith("Maran.", StringComparison.Ordinal));
    }

    /// <summary>Classifies a type for the walk and for the depth census alike.</summary>
    /// <param name="declared">The type, possibly nullable.</param>
    /// <returns>How values of the type are treated.</returns>
    private static ValueKind KindOf(Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type == typeof(string) || type == typeof(Uri) || typeof(IFormFile).IsAssignableFrom(type))
        {
            return ValueKind.Text;
        }

        if (type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type))
        {
            return ValueKind.Json;
        }

        if (type == typeof(SensitiveString) || type == typeof(byte[]) || typeof(Stream).IsAssignableFrom(type)
            || type.IsPrimitive || type.IsEnum)
        {
            return ValueKind.Skipped;
        }

        return ValueKind.Other;
    }

    /// <summary>The types a list or dictionary type holds, or an empty list when it is neither.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The key and value types of a dictionary, the element type of a list, or nothing.</returns>
    private static List<Type> ElementTypesOf(Type type)
    {
        if (type.IsArray)
        {
            return [type.GetElementType()!];
        }

        var interfaces = type.IsInterface ? type.GetInterfaces().Prepend(type).ToList() : type.GetInterfaces().ToList();
        var dictionary = interfaces.FirstOrDefault(candidate =>
        {
            return candidate.IsGenericType
                && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        });
        if (dictionary is not null)
        {
            return [.. dictionary.GetGenericArguments()];
        }

        var sequence = interfaces.FirstOrDefault(candidate =>
        {
            return candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        });
        return sequence is null ? [] : [sequence.GetGenericArguments()[0]];
    }

    /// <summary>The recursive step of <see cref="DeepestTextOf"/>.</summary>
    /// <param name="declared">The type, possibly nullable.</param>
    /// <param name="depth">The depth of a value of this type.</param>
    /// <param name="path">The types being expanded above this one, to find a type that contains itself.</param>
    /// <returns>The deepest depth of a string below, <c>-1</c> for none, <see cref="int.MaxValue"/> for a cycle.</returns>
    private static int DeepestText(Type declared, int depth, HashSet<Type> path)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        switch (KindOf(type))
        {
            case ValueKind.Text:
            case ValueKind.Json:
                return depth;
            case ValueKind.Skipped:
                return -1;
        }

        var elements = ElementTypesOf(type);
        if (elements.Count > 0)
        {
            return elements.Max(element => { return DeepestText(element, depth + 1, path); });
        }

        if (!HoldsMembersWorthReading(type))
        {
            return -1;
        }

        if (!path.Add(type))
        {
            return int.MaxValue;
        }

        var deepest = -1;
        foreach (var property in ReadableProperties(type))
        {
            deepest = Math.Max(deepest, DeepestText(property.PropertyType, depth + 1, path));
        }

        path.Remove(type);
        return deepest;
    }

    /// <summary>The public instance properties of a type that the walk reads: not indexers, not secrets.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The properties.</returns>
    private static IEnumerable<PropertyInfo> ReadableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
            {
                return property.CanRead
                    && property.GetIndexParameters().Length == 0
                    && !SecretNames.Contains(property.Name);
            });
    }

    /// <summary>Walks one value.</summary>
    /// <param name="name">The dotted name of the member the value was read from.</param>
    /// <param name="value">The value.</param>
    /// <param name="depth">How deep the walk is.</param>
    /// <param name="state">What this walk has used.</param>
    /// <returns>The texts inside it.</returns>
    private static IEnumerable<(string Field, string Text)> Walk(string name, object? value, int depth, WalkState state)
    {
        if (value is null)
        {
            yield break;
        }

        state.Enter(name, depth);

        switch (KindOf(value.GetType()))
        {
            case ValueKind.Text:
                yield return (name, TextOf(value));
                yield break;
            case ValueKind.Skipped:
                yield break;
            case ValueKind.Json when value is JsonElement element:
                foreach (var found in WalkJson(name, element, depth, state))
                {
                    yield return found;
                }

                yield break;
            case ValueKind.Json when value is JsonValue jsonValue:
                if (jsonValue.TryGetValue<JsonElement>(out var wrapped))
                {
                    foreach (var found in WalkJson(name, wrapped, depth, state))
                    {
                        yield return found;
                    }
                }
                else if (jsonValue.TryGetValue<string>(out var jsonText))
                {
                    yield return (name, jsonText);
                }

                yield break;
        }

        // A repeat has no new text: its strings were returned the first time. Value types are copies, not repeats.
        if (!value.GetType().IsValueType && !state.Visited.Add(value))
        {
            yield break;
        }

        switch (value)
        {
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    foreach (var found in Walk(name, entry.Key, depth + 1, state))
                    {
                        yield return found;
                    }

                    foreach (var found in Walk(name, entry.Value, depth + 1, state))
                    {
                        yield return found;
                    }
                }

                yield break;
            case IEnumerable sequence:
                foreach (var item in sequence)
                {
                    foreach (var found in Walk(name, item, depth + 1, state))
                    {
                        yield return found;
                    }
                }

                yield break;
        }

        var type = value.GetType();
        if (!HoldsMembersWorthReading(type))
        {
            yield break;
        }

        foreach (var property in ReadableProperties(type))
        {
            var member = $"{name}.{property.Name}";
            foreach (var found in Walk(member, ReadProperty(property, value, member), depth + 1, state))
            {
                yield return found;
            }
        }
    }

    /// <summary>The text of a value <see cref="KindOf"/> called text.</summary>
    /// <param name="value">A string, a URI or an uploaded file.</param>
    /// <returns>The string itself, the URI as written, or the file's name.</returns>
    private static string TextOf(object value)
    {
        return value switch
        {
            Uri uri => uri.OriginalString,
            IFormFile file => file.FileName,
            _ => (string)value,
        };
    }

    /// <summary>Reads a property, turning a getter that throws into a refusal rather than a server error.</summary>
    /// <param name="property">The property.</param>
    /// <param name="owner">The object that holds it.</param>
    /// <param name="member">The dotted name of the member.</param>
    /// <returns>The value.</returns>
    /// <exception cref="DemoFreeTextUnreadableException">The getter threw.</exception>
    private static object? ReadProperty(PropertyInfo property, object owner, string member)
    {
        try
        {
            return property.GetValue(owner);
        }
        catch (TargetInvocationException failure)
        {
            throw new DemoFreeTextUnreadableException(member, "its getter threw", failure.InnerException);
        }
    }

    /// <summary>Walks a JSON value: every string, and the names of the properties of an object.</summary>
    /// <param name="name">The dotted name of the member the value was read from.</param>
    /// <param name="element">The value.</param>
    /// <param name="depth">How deep the walk is.</param>
    /// <param name="state">What this walk has used.</param>
    /// <returns>The texts inside it.</returns>
    private static IEnumerable<(string Field, string Text)> WalkJson(
        string name,
        JsonElement element,
        int depth,
        WalkState state)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return (name, element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    state.Enter(name, depth + 1);
                    yield return (name, property.Name);
                    foreach (var found in WalkJson(name, property.Value, depth + 1, state))
                    {
                        yield return found;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    state.Enter(name, depth + 1);
                    foreach (var found in WalkJson(name, item, depth + 1, state))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }
}
