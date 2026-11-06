using System.Globalization;
using System.Resources;
using Maran.Modules.Identity.Commands.CreateApiKey;

namespace Maran.Modules.Identity.Tests.Resources;

/// <summary>
/// Every error code and display name the API key feature adds exists in english, russian and
/// armenian, and each translation is worded differently from the english text.
/// </summary>
/// <remarks>
/// Inequality against the neutral text is the axis that can go blind: a resource lookup silently falls
/// back to the neutral value when a satellite file lacks the key, so "a value came back" would pass with
/// the whole translation deleted. The key sets of the three files are held equal for the whole module by
/// <c>ResourceKeyParityTests</c> in the architecture tests; this class pins that THESE keys are present
/// and translated.
/// </remarks>
public sealed class ApiKeyTextTests
{
    private static readonly ResourceManager ErrorTable =
        new("Maran.Modules.Identity.Resources.ErrorMessages", typeof(CreateApiKeyCommandValidator).Assembly);

    private static readonly ResourceManager DisplayTable =
        new("Maran.Modules.Identity.Resources.DisplayNames", typeof(CreateApiKeyCommandValidator).Assembly);

    /// <summary>The error codes the API key operations answer with.</summary>
    private static readonly string[] ErrorKeys =
    [
        "ApiKeyNotFound",
        "ApiKeyNameInvalid",
        "ApiKeyScopeUnknown",
        "ApiKeyCidrInvalid",
        "ApiKeyExpiryInvalid",
        "ApiKeyLimitReached",
        "ApiKeyPrefixUnavailable",
        "ApiKeyStatusFilterInvalid",
    ];

    /// <summary>The display names the feature adds: three audit actions, one name per scope and one per status.</summary>
    private static readonly string[] DisplayKeys =
    [
        "AuditActionApiKeyCreated",
        "AuditActionApiKeyRevoked",
        "AuditActionApiKeyCall",
        "ApiKeyScopeSelfRead",
        "ApiKeyScopeServerRead",
        "ApiKeyStatusActive",
        "ApiKeyStatusRevoked",
        "ApiKeyStatusExpired",
    ];

    /// <summary>Each error code crossed with each translated language.</summary>
    public static TheoryData<string, string> ErrorKeysPerLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in ErrorKeys)
        {
            data.Add(key, "ru");
            data.Add(key, "hy");
        }

        return data;
    }

    /// <summary>Each display name crossed with each translated language.</summary>
    public static TheoryData<string, string> DisplayKeysPerLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in DisplayKeys)
        {
            data.Add(key, "ru");
            data.Add(key, "hy");
        }

        return data;
    }

    /// <summary>Every API key error code has an english sentence and a different one in russian and armenian.</summary>
    /// <param name="key">The error code.</param>
    /// <param name="language">The translated language.</param>
    [Theory]
    [MemberData(nameof(ErrorKeysPerLanguage))]
    public void Every_api_key_error_code_is_translated(string key, string language)
    {
        var english = ErrorTable.GetString(key, CultureInfo.InvariantCulture);
        var translated = ErrorTable.GetString(key, new CultureInfo(language));

        Assert.False(string.IsNullOrWhiteSpace(english), $"ErrorMessages has no english entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.NotEqual(english, translated);
    }

    /// <summary>Every API key display name has an english text and a different one in russian and armenian.</summary>
    /// <param name="key">The display name key.</param>
    /// <param name="language">The translated language.</param>
    [Theory]
    [MemberData(nameof(DisplayKeysPerLanguage))]
    public void Every_api_key_display_name_is_translated(string key, string language)
    {
        var english = DisplayTable.GetString(key, CultureInfo.InvariantCulture);
        var translated = DisplayTable.GetString(key, new CultureInfo(language));

        Assert.False(string.IsNullOrWhiteSpace(english), $"DisplayNames has no english entry named {key}.");
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.NotEqual(english, translated);
    }
}
