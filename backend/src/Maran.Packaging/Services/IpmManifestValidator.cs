using System.Text.RegularExpressions;
using Maran.Packaging.Constants;
using Maran.Packaging.Models;
using Maran.Packaging.Resources;
using Maran.Sdk.Manifests;
using Maran.Sdk.Manifests.Enums;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Packaging.Services;

/// <summary>
/// The rules a package manifest must satisfy, as a pure function. What a module DECLARES (id, versions,
/// schema, permissions, navigation, dependencies) is judged by the Sdk's <see cref="ManifestValidator"/>,
/// the same rules the panel applies to a module it builds, so a package cannot declare what a composed
/// module may not. What only a package has (the payload listing, the library, the interface bundle and
/// the migrations) is judged here. A clean result is evidence about what the manifest says, never about
/// what the code does: a signed module is trusted code (threat note
/// <c>2026-11-07-marketplace-loader-threat-note.md</c>).
/// </summary>
public static class IpmManifestValidator
{
    /// <summary>The longest migration id; EF Core's are a timestamp and a name.</summary>
    private const int MaxMigrationIdLength = 150;

    /// <summary>A file name inside <c>lib/</c>, ending in <c>.dll</c>.</summary>
    private static readonly Regex AssemblyFileName = new(
        @"^[A-Za-z0-9._-]+\.dll$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A migration id: letters, digits and underscore.</summary>
    private static readonly Regex MigrationId = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A short token: an encryption algorithm or a key id.</summary>
    private static readonly Regex Token = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>A locale code such as <c>en</c> or <c>pt-BR</c>.</summary>
    private static readonly Regex Locale = new(
        "^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Lowercase hexadecimal, the spelling of a digest in the manifest.</summary>
    private static readonly Regex LowerHex = new(
        "^[0-9a-f]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Judges a manifest.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="panelVersion">The running panel's version.</param>
    /// <returns>The same manifest on success; otherwise the first refusal.</returns>
    public static Result<IpmManifest> Validate(IpmManifest manifest, SemanticVersion panelVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(panelVersion);

        var error = CheckDeclaration(manifest, panelVersion)
            ?? CheckPanelCeiling(manifest)
            ?? CheckMigrations(manifest)
            ?? CheckFiles(manifest)
            ?? CheckLibrary(manifest)
            ?? CheckSpa(manifest);

        return error is null ? Result<IpmManifest>.Ok(manifest) : Result<IpmManifest>.Fail(error);
    }

    /// <summary>
    /// Runs the Sdk's manifest rules over what the package declares. Whether the modules it names are
    /// actually installed is a fact about an installation, not about the package, and is judged by
    /// <see cref="IpmCompatibility.CheckInstall"/>; here every module the package names is assumed
    /// present, so the Sdk's rules judge the declaration and nothing else.
    /// </summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <param name="panelVersion">The running panel's version.</param>
    /// <returns>Null when the declaration is acceptable; otherwise the refusal.</returns>
    private static Error? CheckDeclaration(IpmManifest manifest, SemanticVersion panelVersion)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { manifest.Id };
        known.UnionWith(manifest.Dependencies.Select(dependency =>
        {
            return dependency.Id;
        }));
        known.UnionWith(manifest.Routes
            .Select(route =>
            {
                return route.RequiresModule;
            })
            .OfType<string>());

        var findings = ManifestValidator.Validate(
            manifest.ToManifest(),
            panelVersion,
            known,
            ManifestPermissionVocabulary.Policies);

        if (findings.Count == 0)
        {
            return null;
        }

        var onlyTooOld = findings.All(finding =>
        {
            return finding.Kind == ManifestFindingKind.MinPanelVersionAbovePanel;
        });

        return Refuse(onlyTooOld ? nameof(ErrorMessages.IpmPanelTooOld) : nameof(ErrorMessages.IpmManifestInvalid));
    }

    /// <summary>Checks the optional newest panel the package declares support for.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <returns>Null when absent or a version not below the minimum; otherwise the refusal.</returns>
    private static Error? CheckPanelCeiling(IpmManifest manifest)
    {
        if (manifest.MaxPanelVersion is null)
        {
            return null;
        }

        if (!SemanticVersion.TryParse(manifest.MaxPanelVersion, out var ceiling)
            || !SemanticVersion.TryParse(manifest.MinPanelVersion, out var floor))
        {
            return Invalid();
        }

        return SemanticVersion.Compare(ceiling!, floor!) >= 0 ? null : Invalid();
    }

    /// <summary>A module that owns a schema carries migrations, and one that owns none carries none.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <returns>Null when consistent; otherwise the refusal.</returns>
    private static Error? CheckMigrations(IpmManifest manifest)
    {
        if ((manifest.SchemaName is null) != (manifest.Migrations is null))
        {
            return Invalid();
        }

        var migrations = manifest.Migrations;

        if (migrations is null)
        {
            return null;
        }

        var ids = new List<string> { migrations.To };

        if (migrations.From is not null)
        {
            ids.Add(migrations.From);
        }

        return ids.All(IsMigrationId) ? null : Invalid();
    }

    /// <summary>Checks every listed file: a contained name inside the layout, listed once, with a well-formed pin.</summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <returns>Null when the listing is acceptable; otherwise the refusal.</returns>
    private static Error? CheckFiles(IpmManifest manifest)
    {
        if (manifest.Files.Count == 0)
        {
            return Invalid();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in manifest.Files)
        {
            var nameError = IpmEntryNameRule.Judge(file.Path);

            if (nameError is not null)
            {
                return nameError;
            }

            if (!seen.Add(file.Path))
            {
                return Refuse(nameof(ErrorMessages.IpmDuplicateEntry));
            }

            if (!IsInLayout(file.Path)
                || file.Sha384.Length != IpmLayout.Sha384HexLength
                || !LowerHex.IsMatch(file.Sha384)
                || file.Size < 0)
            {
                return Invalid();
            }
        }

        return null;
    }

    /// <summary>
    /// Checks the library: a plain package pins <c>lib/&lt;EntryAssembly&gt;</c> and no encrypted entry; an
    /// encrypted one pins <c>lib.enc</c> and nothing under <c>lib/</c>.
    /// </summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <returns>Null when consistent; otherwise the refusal.</returns>
    private static Error? CheckLibrary(IpmManifest manifest)
    {
        if (!AssemblyFileName.IsMatch(manifest.EntryAssembly))
        {
            return Invalid();
        }

        var hasEncrypted = Pins(manifest, IpmLayout.EncryptedLibraryEntry);
        var hasPlainLibrary = manifest.Files.Any(file =>
        {
            return file.Path.StartsWith(IpmLayout.LibraryPrefix, StringComparison.Ordinal);
        });

        if (manifest.Encryption is null)
        {
            return !hasEncrypted && Pins(manifest, IpmLayout.LibraryPrefix + manifest.EntryAssembly) ? null : Invalid();
        }

        var encryption = manifest.Encryption;

        return hasEncrypted && !hasPlainLibrary && Token.IsMatch(encryption.Alg) && Token.IsMatch(encryption.KeyId)
            ? null
            : Invalid();
    }

    /// <summary>
    /// Checks the interface bundle: files under <c>spa/</c> exist only if the bundle is declared, its
    /// entry is one of them, its integrity value is the entry's own pin, and every translation it
    /// names, the fallback among them, is pinned.
    /// </summary>
    /// <param name="manifest">The manifest to judge.</param>
    /// <returns>Null when consistent; otherwise the refusal.</returns>
    private static Error? CheckSpa(IpmManifest manifest)
    {
        var spa = manifest.Spa;
        var hasSpaFiles = manifest.Files.Any(file =>
        {
            return file.Path.StartsWith(IpmLayout.SpaPrefix, StringComparison.Ordinal);
        });

        if (spa is null)
        {
            return hasSpaFiles ? Invalid() : null;
        }

        var entry = manifest.Files.FirstOrDefault(file =>
        {
            return string.Equals(file.Path, spa.Entry, StringComparison.Ordinal);
        });

        if (entry is null
            || !spa.Entry.StartsWith(IpmLayout.SpaPrefix, StringComparison.Ordinal)
            || !string.Equals(spa.Integrity, IntegrityOf(entry), StringComparison.Ordinal))
        {
            return Invalid();
        }

        var locales = spa.Locales;

        if (!locales.Contains(IpmLayout.FallbackLocale)
            || locales.Distinct(StringComparer.Ordinal).Count() != locales.Count)
        {
            return Invalid();
        }

        return locales.All(locale =>
        {
            return Locale.IsMatch(locale) && Pins(manifest, IpmLayout.SpaLocalesPrefix + locale + ".json");
        })
            ? null
            : Invalid();
    }

    /// <summary>The Subresource Integrity value for a pinned file: <c>sha384-</c> and the digest in base64.</summary>
    /// <param name="file">The pinned file; its digest was checked to be lowercase hexadecimal.</param>
    /// <returns>The value an interface bundle must declare for that file.</returns>
    private static string IntegrityOf(IpmFileEntry file)
    {
        return IpmLayout.IntegrityPrefix + Convert.ToBase64String(Convert.FromHexString(file.Sha384));
    }

    /// <summary>True when a path is in the layout: the encrypted library, or under <c>lib/</c> or <c>spa/</c>.</summary>
    /// <param name="path">A contained entry name.</param>
    /// <returns>True when the format allows a payload file there.</returns>
    private static bool IsInLayout(string path)
    {
        return string.Equals(path, IpmLayout.EncryptedLibraryEntry, StringComparison.Ordinal)
            || path.StartsWith(IpmLayout.LibraryPrefix, StringComparison.Ordinal)
            || path.StartsWith(IpmLayout.SpaPrefix, StringComparison.Ordinal);
    }

    /// <summary>True when the manifest pins a file at exactly this path.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="path">The entry name.</param>
    /// <returns>True when listed.</returns>
    private static bool Pins(IpmManifest manifest, string path)
    {
        return manifest.Files.Any(file =>
        {
            return string.Equals(file.Path, path, StringComparison.Ordinal);
        });
    }

    /// <summary>True when the text is a migration id.</summary>
    /// <param name="id">The id to test.</param>
    /// <returns>True when well formed and not over-long.</returns>
    private static bool IsMigrationId(string id)
    {
        return id.Length <= MaxMigrationIdLength && MigrationId.IsMatch(id);
    }

    /// <summary>The refusal for a manifest that breaks the rules.</summary>
    /// <returns>The error.</returns>
    private static Error Invalid()
    {
        return Refuse(nameof(ErrorMessages.IpmManifestInvalid));
    }

    /// <summary>A validation error from a code.</summary>
    /// <param name="code">A key of the resx.</param>
    /// <returns>The error.</returns>
    private static Error Refuse(string code)
    {
        return Error.Of(code, ErrorType.Validation);
    }
}
