namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>What <see cref="Policies.BrandingAccentPolicy"/> decided about a submitted accent colour.</summary>
public enum BrandingAccentVerdict
{
    /// <summary>The colour is a <c>#rrggbb</c> value that carries white text at the contrast the panel needs.</summary>
    Acceptable,

    /// <summary>The value is not a <c>#</c> followed by exactly six hexadecimal digits.</summary>
    NotAColor,

    /// <summary>The colour is well formed but too light for white text to be read on it.</summary>
    LowContrast,
}
