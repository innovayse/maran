using System.Reflection;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Enumerates every audit action this build can record — not the ones one file happens to list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the Sdk's <see cref="AuditActions"/> is not the source of truth.</b> It reads like one,
/// and a guard walking it reports full coverage over a set that can be missing the very actions the
/// panel writes: <c>Maran.Modules.Ftp</c> declares its six action names on its own journal, so a
/// walk of <see cref="AuditActions"/> found no gap while the audit screen printed
/// <c>FtpUserCreated</c> to a customer in all three languages. That is the shape rules/testing.md
/// forbids — a check that cannot observe what it reports on — and moving those six constants into
/// the Sdk would have closed the six holes while leaving the mechanism intact for the seventh.
/// </para>
/// <para>
/// <b>What IS the source of truth.</b> An action reaches the journal through a module's
/// <c>*AuditJournal</c> — <c>maran structure</c> check 6c keeps audit-entry construction inside
/// those types — so the actions this build can record are the Sdk's constants together with every
/// action constant the journals declare. Both are read by reflection from the assemblies sitting
/// beside the test binary, so a module written after this type, and a module whose author parks its
/// constants locally as the Ftp module deliberately did, are covered without anybody being told.
/// </para>
/// <para>
/// UNOBSERVED HERE, said plainly rather than left to be discovered:
/// </para>
/// <list type="bullet">
/// <item>
/// An action passed to a journal as a string LITERAL at the call site declares no constant, so
/// nothing here can enumerate it. Every call site in this repository passes a constant, and that is
/// a property of the code rather than of this type.
/// </item>
/// <item>
/// A marketplace module's actions live in an assembly this build never compiled against and is not
/// deployed beside these tests. They are what <c>AuditActionDisplayNames</c>'s fallback exists for.
/// </item>
/// <item>
/// A constant that is declared and never passed to a journal is still demanded a display name here.
/// That over-approximation is deliberate: the cheap fix is to delete a dead constant, and the
/// alternative — reading call sites — would make the guard blind exactly where a new action is
/// added without a call site yet.
/// </item>
/// </list>
/// </remarks>
public static class AuditActionVocabulary
{
    /// <summary>The type-name suffix that makes a type a module's audit journal.</summary>
    public const string JournalSuffix = "AuditJournal";

    /// <summary>The prefix an action's display-name key carries, from <c>AuditActionDisplayNames</c>.</summary>
    public const string KeyPrefix = "AuditAction";

    /// <summary>Every module audit journal in the loaded product assemblies.</summary>
    /// <returns>The journal types, ordered by full name.</returns>
    public static IReadOnlyList<Type> Journals()
    {
        return DisplayNameLaw.LoadedProductAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name ?? string.Empty;
                return name.StartsWith("Maran.Modules.", StringComparison.Ordinal);
            })
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsPublic && type.Name.EndsWith(JournalSuffix, StringComparison.Ordinal);
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every public string constant an audit journal declares, with where it was declared.</summary>
    /// <returns>
    /// One entry per constant: the qualified member name — <c>Namespace.Type.Member</c> — mapped to
    /// the constant's value. Qualified, so an exemption and a failure message both name one member
    /// and not merely a string that two types could have spelled the same.
    /// </returns>
    public static IReadOnlyDictionary<string, string> JournalConstants()
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var journal in Journals())
        {
            foreach (var value in StringConstantsOf(journal))
            {
                declared[$"{journal.FullName}.{value.Key}"] = value.Value;
            }
        }

        return declared;
    }

    /// <summary>The Sdk's shared action constants.</summary>
    /// <returns>The action values <see cref="AuditActions"/> declares.</returns>
    public static IReadOnlyList<string> SharedActions()
    {
        return StringConstantsOf(typeof(AuditActions))
            .Select(constant =>
            {
                return constant.Value;
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The public string constants of one type, by member name.</summary>
    /// <param name="type">The type to read.</param>
    /// <returns>Member name to constant value.</returns>
    private static Dictionary<string, string> StringConstantsOf(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field =>
            {
                return field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string);
            })
            .ToDictionary(
                field =>
                {
                    return field.Name;
                },
                field =>
                {
                    return (string)field.GetRawConstantValue()!;
                },
                StringComparer.Ordinal);
    }
}
