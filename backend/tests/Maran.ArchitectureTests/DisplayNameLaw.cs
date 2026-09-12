using System.Reflection;

namespace Maran.ArchitectureTests;

/// <summary>
/// The rule <see cref="DisplayNameLawTests"/> enforces: which members of an outward DTO carry a
/// contract vocabulary value, and what counts as the localized name that must travel beside one.
/// </summary>
/// <remarks>
/// <para>
/// It is a class of its own rather than private methods on the test, because the test applies it
/// to two different type sets — the product's DTOs, and the reconstructed defects in
/// <c>Fixtures/DisplayNames/</c> — and a law proved against a copy of itself proves nothing.
/// </para>
/// <para>
/// <b>The property.</b> A screen never prints a value the backend authored in English unless the
/// backend also authored the localized text for it. Structurally: a member whose value comes from
/// a closed vocabulary the panel owns — an enum, or a <see cref="string"/> the wire uses as an
/// identifier — must have a sibling member on the same DTO holding that value's name in the
/// caller's language. The alternative outs are declared, one at a time, in
/// <see cref="DisplayNameExemptions"/>; this class knows nothing about them, so the shape of a
/// violation stays the same whether or not somebody has excused it.
/// </para>
/// <para>
/// <b>Why the two member kinds are not treated alike.</b> An enum is closed on the wire, so the SPA
/// CAN enumerate it and own its wording — that is a defensible arrangement and the exemption
/// register verifies it rather than believing it. A <see cref="string"/> identifier is open: the
/// panel adds task kinds, the agent adds error codes, a marketplace module adds audit actions, and
/// no bundle shipped before them can hold their words. So a string vocabulary member has no
/// SPA-owned route at all — the backend names it or nobody does. Three of the five defects this
/// law was written for were string members, and that asymmetry is why they kept recurring.
/// </para>
/// </remarks>
public static class DisplayNameLaw
{
    /// <summary>
    /// The name endings that make a <see cref="string"/> member an identifier rather than prose.
    /// </summary>
    /// <remarks>
    /// Deliberately endings and not whole names: <c>FailureCode</c>, <c>LastRenewalErrorCode</c> and
    /// <c>Kind</c> are all the same thing wearing a longer name. <c>Name</c> is NOT here — most
    /// <c>Name</c> members hold what an operator typed, and demanding a translation of a customer's
    /// own words would be the opposite of this law. The one <c>Name</c> that WAS a defect — the
    /// seeded backup destination — is caught through the <c>Kind</c> that sits beside it, which is
    /// the member that says the row is one the panel authored.
    /// </remarks>
    private static readonly string[] IdentifierEndings =
    [
        "Code",
        "Kind",
        "Action",
        "Reason",
        "Type",
        "Status",
        "State",
        "Event",
        "Operation",
    ];

    /// <summary>The endings a sibling name may add to a member's name to be its display name.</summary>
    private static readonly string[] DisplayEndings = ["DisplayName", "Name", "Label", "Text"];

    /// <summary>
    /// The endings stripped from a member's name before looking for its sibling, so that
    /// <c>FailureCode</c> is served by <c>FailureDisplayName</c> and not only by
    /// <c>FailureCodeDisplayName</c>.
    /// </summary>
    private static readonly string[] StrippableEndings = ["Code", "Kind", "Type"];

    /// <summary>Every outward view type the law judges, across the loaded product assemblies.</summary>
    /// <returns>The public <c>*Dto</c> types of the modules and of the host, ordered by full name.</returns>
    /// <remarks>
    /// The agent client's <c>*Dto</c> types are excluded on purpose: they are transport carriers
    /// between the panel and the agent, they reach no screen, and requiring a localized name on one
    /// would push translation into the layer that talks to a root daemon.
    /// </remarks>
    public static IReadOnlyList<Type> OutwardDtoTypes()
    {
        return LoadedProductAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name ?? string.Empty;
                return name.StartsWith("Maran.Modules.", StringComparison.Ordinal)
                    || string.Equals(name, "Maran.Host", StringComparison.Ordinal);
            })
            .SelectMany(assembly =>
            {
                return assembly.GetTypes();
            })
            .Where(type =>
            {
                return type.IsPublic && type.Name.EndsWith("Dto", StringComparison.Ordinal);
            })
            .OrderBy(type =>
            {
                return type.FullName;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Names the members of one DTO that carry a vocabulary value with no name beside it.</summary>
    /// <param name="dto">The outward view type to judge.</param>
    /// <returns>The offending member names, ordered; empty when the type obeys the law.</returns>
    public static IReadOnlyList<string> UnnamedVocabularyMembers(Type dto)
    {
        var properties = ReadableProperties(dto);
        return properties
            .Where(property =>
            {
                return IsVocabularyMember(property);
            })
            .Where(property =>
            {
                return !HasDisplaySibling(dto, property, properties);
            })
            .Select(property =>
            {
                return property.Name;
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether one member of a DTO carries a contract vocabulary value.</summary>
    /// <param name="property">The member to judge.</param>
    /// <returns><c>true</c> when the member's value is an enum or a wire identifier.</returns>
    public static bool IsVocabularyMember(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type.IsEnum)
        {
            return true;
        }

        if (type != typeof(string))
        {
            return false;
        }

        // A display sibling is itself a string, and several of them end in "Text" or "Label" —
        // never let the law flag the very member that answers it.
        if (IsDisplayName(property.Name))
        {
            return false;
        }

        return IdentifierEndings.Any(ending =>
        {
            return property.Name.EndsWith(ending, StringComparison.Ordinal);
        });
    }

    /// <summary>The sibling names that would satisfy the law for one vocabulary member.</summary>
    /// <param name="dto">The type the member belongs to.</param>
    /// <param name="property">The vocabulary member.</param>
    /// <returns>Every acceptable name, so a failure message can print the shortest fix.</returns>
    /// <remarks>
    /// The bare <c>DisplayName</c>/<c>Name</c> forms are accepted only for the member that IS the
    /// thing the DTO describes — <c>ServiceStatusDto.Service</c>, whose display sibling is called
    /// <c>Name</c> because "the name" of a service status row can mean nothing else. Accepting a
    /// bare <c>Name</c> for every member would have made the broken backup destination pass: it
    /// carried a <c>Name</c> all along, and that <c>Name</c> was the defect.
    /// </remarks>
    public static IReadOnlyList<string> AcceptedSiblingNames(Type dto, PropertyInfo property)
    {
        var bases = new List<string> { property.Name };
        foreach (var ending in StrippableEndings)
        {
            if (property.Name.Length > ending.Length && property.Name.EndsWith(ending, StringComparison.Ordinal))
            {
                bases.Add(property.Name[..^ending.Length]);
            }
        }

        var names = bases
            .SelectMany(prefix =>
            {
                return DisplayEndings.Select(ending =>
                {
                    return prefix + ending;
                });
            })
            .ToList();

        if (IsSubjectMember(dto, property))
        {
            names.AddRange(DisplayEndings.Where(ending =>
            {
                return !string.Equals(ending, "Text", StringComparison.Ordinal);
            }));
        }

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Finds one member of a DTO by name, or <c>null</c> when it declares none.</summary>
    /// <param name="dto">The type to look in.</param>
    /// <param name="memberName">The member's name.</param>
    /// <returns>The member, or <c>null</c>.</returns>
    public static PropertyInfo? MemberOf(Type dto, string memberName)
    {
        return ReadableProperties(dto).FirstOrDefault(property =>
        {
            return string.Equals(property.Name, memberName, StringComparison.Ordinal);
        });
    }

    /// <summary>Loads every product assembly sitting beside the test binary.</summary>
    /// <returns>The loaded <c>Maran.*</c> assemblies, the test binary excluded.</returns>
    /// <remarks>
    /// The output directory rather than the compiler's reference list, for the reason
    /// <see cref="ModuleCoverageTests"/> gives: a ProjectReference whose types are never mentioned
    /// is dropped from the reference list, and the module would then be invisible to a law that
    /// exists to see it.
    /// </remarks>
    public static IReadOnlyList<Assembly> LoadedProductAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // Native or mixed-mode files matching the pattern are not managed assemblies.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return name is not null
                    && name.StartsWith("Maran", StringComparison.Ordinal)
                    && !name.EndsWith("Tests", StringComparison.Ordinal);
            })
            .Distinct()
            .ToList();
    }

    /// <summary>The public instance members of a DTO, its compiler-generated ones excluded.</summary>
    /// <param name="dto">The type to read.</param>
    /// <returns>The members the law judges.</returns>
    private static List<PropertyInfo> ReadableProperties(Type dto)
    {
        return dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
            {
                // Records synthesize EqualityContract; it is not part of the wire shape.
                return !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal);
            })
            .ToList();
    }

    /// <summary>Whether a vocabulary member has an acceptable display sibling on its own DTO.</summary>
    /// <param name="dto">The type the member belongs to.</param>
    /// <param name="property">The vocabulary member.</param>
    /// <param name="properties">Every member of the type, already read.</param>
    /// <returns><c>true</c> when a string sibling names the value for an operator.</returns>
    private static bool HasDisplaySibling(Type dto, PropertyInfo property, List<PropertyInfo> properties)
    {
        var accepted = AcceptedSiblingNames(dto, property);
        return properties.Any(candidate =>
        {
            return candidate.PropertyType == typeof(string)
                && accepted.Contains(candidate.Name, StringComparer.Ordinal);
        });
    }

    /// <summary>Whether a member names the very thing its DTO is a view of.</summary>
    /// <param name="dto">The type the member belongs to.</param>
    /// <param name="property">The member to judge.</param>
    /// <returns><c>true</c> when the type's subject begins with the member's name.</returns>
    private static bool IsSubjectMember(Type dto, PropertyInfo property)
    {
        var subject = dto.Name.EndsWith("Dto", StringComparison.Ordinal) ? dto.Name[..^3] : dto.Name;
        return subject.StartsWith(property.Name, StringComparison.Ordinal);
    }

    /// <summary>Whether a member name reads as a display name rather than as data.</summary>
    /// <param name="memberName">The member's name.</param>
    /// <returns><c>true</c> when the name ends in one of the display endings.</returns>
    private static bool IsDisplayName(string memberName)
    {
        return DisplayEndings.Any(ending =>
        {
            return memberName.EndsWith(ending, StringComparison.Ordinal);
        });
    }
}
