using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Packaging.Models;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Reads and writes <c>ipm.json</c>. The signature is over the file as stored, so there is no
/// canonical form: the writer emits the record's properties in declaration order with no whitespace,
/// and the reader checks the signature over the bytes it was given before it parses them. Reading is
/// strict, because lenient parsing is where two readers disagree about one signed file: a duplicate
/// member, an unknown member, a missing required member, a null where none is allowed and a number
/// where a name belongs are all refused.
/// </summary>
public static class IpmManifestCodec
{
    /// <summary>The deepest nesting the reader follows; the manifest is shallow by construction.</summary>
    private const int MaxDepth = 16;

    /// <summary>The name of the member read before the rest, so an unknown revision is reported as such.</summary>
    private const string FormatVersionMember = "formatVersion";

    /// <summary>The one set of options both directions use.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = MaxDepth,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>The options of the first, shallow look at the document.</summary>
    private static readonly JsonDocumentOptions PeekOptions = new()
    {
        MaxDepth = MaxDepth,
    };

    /// <summary>Reads a manifest.</summary>
    /// <param name="bytes">The stored bytes of <c>ipm.json</c>, already authenticated.</param>
    /// <returns>The manifest, or <c>IpmFormatVersionUnknown</c> for a revision this library does not read, or <c>IpmManifestMalformed</c>.</returns>
    public static Result<IpmManifest> Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (HasDuplicateMember(bytes))
            {
                return Malformed();
            }

            var formatError = CheckFormat(bytes);

            if (formatError is not null)
            {
                return Result<IpmManifest>.Fail(formatError);
            }

            var manifest = JsonSerializer.Deserialize<IpmManifest>(bytes, Options);

            return manifest is null ? Malformed() : Result<IpmManifest>.Ok(manifest);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return Malformed();
        }
    }

    /// <summary>Writes a manifest as the bytes that are signed and stored.</summary>
    /// <param name="manifest">The manifest to write.</param>
    /// <returns>The UTF-8 bytes of <c>ipm.json</c>, byte-identical for equal manifests.</returns>
    public static byte[] Serialize(IpmManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    /// <summary>
    /// True when any object in the document names the same member twice. The serializer would keep the
    /// last one silently, and a signed file that two readers read differently is a way to show one
    /// reader a value the other never sees.
    /// </summary>
    /// <param name="bytes">The manifest bytes.</param>
    /// <returns>True when a member name repeats inside one object.</returns>
    private static bool HasDuplicateMember(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = MaxDepth });
        var scopes = new Stack<HashSet<string>?>();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.StartArray:
                    scopes.Push(null);
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    scopes.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    if (!scopes.Peek()!.Add(reader.GetString()!))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>Reads only <c>formatVersion</c> and judges it, ahead of any rule about the other members.</summary>
    /// <param name="bytes">The manifest bytes.</param>
    /// <returns>Null when the revision is read by this library; otherwise the refusal.</returns>
    private static Error? CheckFormat(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray(), PeekOptions);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(FormatVersionMember, out var member)
            || member.ValueKind != JsonValueKind.Number
            || !member.TryGetInt32(out var formatVersion))
        {
            return Malformed().Error;
        }

        return IpmCompatibility.CheckFormat(formatVersion).Error;
    }

    /// <summary>The refusal for a manifest that cannot be read.</summary>
    /// <returns>The failed result.</returns>
    private static Result<IpmManifest> Malformed()
    {
        return Result<IpmManifest>.Fail(Error.Of(nameof(ErrorMessages.IpmManifestMalformed), ErrorType.Validation));
    }
}
