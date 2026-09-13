using System.Reflection;
using System.Text.Json;
using Maran.ArchitectureTests.Fixtures.DisplayNames;

namespace Maran.ArchitectureTests;

/// <summary>
/// Enforces rules/architecture.md "The backend owns the data, the SPA renders it": a contract
/// constant never reaches an operator's screen as the name of the thing it identifies.
/// </summary>
/// <remarks>
/// <para>
/// The rule already existed and was broken five times in two days — a task kind printed as
/// <c>BackupCreate</c>, a backup failure as <c>AgentSystemFailure</c>, an English
/// <c>Local storage</c> inside a Russian panel, the monitored services as <c>webServer</c> and
/// <c>cron</c>, and the audit journal as <c>BackupRestored</c>. Each was fixed one screen at a
/// time, which is why there was a fifth. This suite is the rule made mechanical
/// (rules/README.md, "Mechanical enforcement"): the property is defined in
/// <see cref="DisplayNameLaw"/>, the excuses are declared with evidence in
/// <see cref="DisplayNameExemptions"/>, and every one of the five defects is reconstructed in
/// <c>Fixtures/DisplayNames/</c> and shown to be refused — because a law that passes against the
/// defects it was written for is worth less than no law, this repository having already shipped
/// one such check.
/// </para>
/// <para>
/// UNOBSERVED HERE: the law reads the wire shape, not the rendering. It cannot see a screen that
/// prints the machine member while ignoring the display name beside it, and it cannot see a value
/// the backend localizes into the wrong language. What it does see is the only thing that made all
/// five defects possible — a vocabulary value leaving the panel with no name attached.
/// </para>
/// </remarks>
public sealed class DisplayNameLawTests
{
    /// <summary>
    /// The outward DTOs whose absence would make this suite vacuous: the five types the five
    /// shipped defects were found in, plus the host's module list.
    /// </summary>
    /// <remarks>
    /// A guard on the axis that can actually go blind (rules/testing.md). Counting types would not
    /// do it: a filter that stopped matching one module, or a namespace that moved, leaves a
    /// plausible count behind. These six names are what the law exists to judge, so if the scan no
    /// longer contains them it is not judging anything that matters.
    /// </remarks>
    private static readonly string[] TypesTheLawMustSee =
    [
        "Maran.Modules.Tasks.Common.PanelTaskDto",
        "Maran.Modules.Backups.Common.BackupDto",
        "Maran.Modules.Backups.Common.BackupDestinationDto",
        "Maran.Modules.Monitoring.Common.ServiceStatusDto",
        "Maran.Modules.Identity.Common.AuditEventDto",
        "Maran.Host.Modules.ModuleDto",
    ];

    /// <summary>Every contract vocabulary member of an outward dto carries a localized name.</summary>
    [Fact]
    public void Every_contract_vocabulary_member_of_an_outward_dto_carries_a_localized_name()
    {
        var excused = DisplayNameExemptions.Declared
            .Select(exemption =>
            {
                return $"{exemption.DtoTypeName}.{exemption.MemberName}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var dto in DisplayNameLaw.OutwardDtoTypes())
        {
            foreach (var member in DisplayNameLaw.UnnamedVocabularyMembers(dto))
            {
                if (excused.Contains($"{dto.FullName}.{member}"))
                {
                    continue;
                }

                violations.Add(Describe(dto, member));
            }
        }

        Assert.True(
            violations.Count == 0,
            "A value the panel authored is leaving the backend with no name an operator can read, "
            + "which is how a Russian screen comes to say 'AgentSystemFailure' "
            + "(rules/architecture.md, 'The backend owns the data, the SPA renders it'):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>The display name law reads the outward dtos it claims to judge.</summary>
    [Fact]
    public void The_display_name_law_reads_the_outward_dtos_it_claims_to_judge()
    {
        var scanned = DisplayNameLaw.OutwardDtoTypes()
            .Select(type =>
            {
                return type.FullName;
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = TypesTheLawMustSee.Where(name =>
        {
            return !scanned.Contains(name);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "The law found none of these outward DTOs, so it is reporting a clean tree because it "
            + "is looking at nothing: "
            + string.Join(", ", missing)
            + ". A module project dropped out of the test build, or a DTO moved out of its "
            + "module's Common/ namespace, or the *Dto naming ended. Fix the scan, not this list.");

        // Not a pinned total, which would fail on every new DTO for no reason; a floor that a
        // half-loaded assembly set cannot clear.
        Assert.True(
            scanned.Count >= 30,
            $"Only {scanned.Count} outward DTOs were found; the panel has far more than that, so "
            + "the scan is partial.");
    }

    /// <summary>Every exemption names a member the law would otherwise refuse.</summary>
    [Fact]
    public void Every_exemption_names_a_member_the_law_would_otherwise_refuse()
    {
        var byName = DisplayNameLaw.OutwardDtoTypes().ToDictionary(
            type =>
            {
                return type.FullName ?? type.Name;
            },
            type =>
            {
                return type;
            },
            StringComparer.Ordinal);

        var stale = new List<string>();
        foreach (var exemption in DisplayNameExemptions.Declared)
        {
            if (!byName.TryGetValue(exemption.DtoTypeName, out var dto))
            {
                stale.Add($"{exemption.DtoTypeName} no longer exists; delete its exemption.");
                continue;
            }

            if (!DisplayNameLaw.UnnamedVocabularyMembers(dto).Contains(exemption.MemberName, StringComparer.Ordinal))
            {
                stale.Add(
                    $"{exemption.DtoTypeName}.{exemption.MemberName} no longer needs excusing — it "
                    + "was given a display name, renamed, or removed. Delete the entry.");
            }

            if (exemption.Reason.Length < 40)
            {
                stale.Add($"{exemption.DtoTypeName}.{exemption.MemberName} carries no real reason.");
            }
        }

        Assert.True(
            stale.Count == 0,
            "The exemption register has rotted. It is one-way — an entry that has stopped being "
            + "needed is deleted, never left behind, or the register becomes a list of things that "
            + "used to be wrong and nobody rereads it:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    /// <summary>Every spa owned exemption names a key that exists in english russian and armenian.</summary>
    [Fact]
    public void Every_spa_owned_exemption_names_a_key_that_exists_in_english_russian_and_armenian()
    {
        var keysByLanguage = SpaLocaleKeys.Languages.ToDictionary(
            language =>
            {
                return language;
            },
            language =>
            {
                return SpaLocaleKeys.KeysOf(language);
            },
            StringComparer.Ordinal);

        var problems = new List<string>();
        var checkedKeys = 0;
        foreach (var exemption in DisplayNameExemptions.Declared)
        {
            if (exemption.Kind != DisplayNameExemptionKind.SpaOwnedVocabulary)
            {
                continue;
            }

            var member = MemberOfDeclaredType(exemption);
            var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
            if (!type.IsEnum)
            {
                problems.Add(
                    $"{exemption.DtoTypeName}.{exemption.MemberName} is a {type.Name}, and only a "
                    + "closed set may be delegated to the SPA: a bundle cannot hold a word for a "
                    + "value invented after it shipped. Name it on the backend instead.");
                continue;
            }

            foreach (var value in Enum.GetNames(type))
            {
                var key = exemption.Evidence.Replace("{member}", JsonNamingPolicy.CamelCase.ConvertName(value), StringComparison.Ordinal);
                foreach (var (language, keys) in keysByLanguage)
                {
                    checkedKeys++;
                    if (!keys.Contains(key))
                    {
                        problems.Add(
                            $"{exemption.DtoTypeName}.{exemption.MemberName} is excused because the "
                            + $"SPA words it, but frontend/src/locales/{language} has no '{key}' for "
                            + $"{type.Name}.{value}. Add the key in all three languages, or give the "
                            + "member a display name on the wire and delete the exemption.");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // The evidence check is itself a search that can silently match nothing.
        Assert.True(checkedKeys > 0, "No SPA-owned exemption was verified, so this test proved nothing.");
    }

    /// <summary>Every exemption anchored to the spa names a file that still mentions the member.</summary>
    [Fact]
    public void Every_exemption_anchored_to_the_spa_names_a_file_that_still_mentions_the_member()
    {
        var problems = new List<string>();
        var checkedFiles = 0;
        foreach (var exemption in DisplayNameExemptions.Declared)
        {
            if (exemption.Kind == DisplayNameExemptionKind.SpaOwnedVocabulary)
            {
                continue;
            }

            var path = Path.Combine(SpaLocaleKeys.RepositoryRoot(), exemption.Evidence.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                problems.Add($"{exemption.DtoTypeName}.{exemption.MemberName} cites {exemption.Evidence}, which does not exist.");
                continue;
            }

            checkedFiles++;
            var wireName = JsonNamingPolicy.CamelCase.ConvertName(exemption.MemberName);
            if (!File.ReadAllText(path).Contains(wireName, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{exemption.DtoTypeName}.{exemption.MemberName} cites {exemption.Evidence}, "
                    + $"which no longer mentions '{wireName}'. The screen it was excused against "
                    + "has changed; re-decide the member rather than keeping the entry.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(checkedFiles > 0, "No anchored exemption was verified, so this test proved nothing.");
    }

    /// <summary>Each of the five shipped defects is refused by the law.</summary>
    [Fact]
    public void Each_of_the_five_shipped_defects_is_refused_by_the_law()
    {
        Assert.Equal(["Kind"], DisplayNameLaw.UnnamedVocabularyMembers(typeof(PanelTaskBeforeTheFixDto)));
        Assert.Equal(["FailureCode", "Status"], DisplayNameLaw.UnnamedVocabularyMembers(typeof(BackupBeforeTheFixDto)));
        Assert.Equal(["Kind"], DisplayNameLaw.UnnamedVocabularyMembers(typeof(BackupDestinationBeforeTheFixDto)));
        Assert.Equal(["Service", "State"], DisplayNameLaw.UnnamedVocabularyMembers(typeof(ServiceStatusBeforeTheFixDto)));
        Assert.Equal(["Action"], DisplayNameLaw.UnnamedVocabularyMembers(typeof(AuditEventBeforeTheFixDto)));
    }

    /// <summary>The laws refusal names the type the member and the change that fixes it.</summary>
    [Fact]
    public void The_laws_refusal_names_the_type_the_member_and_the_change_that_fixes_it()
    {
        // A law whose failure message does not tell the engineer which type broke it, and what to
        // add, is half a law: the reader's next move is to reverse-engineer the rule from its
        // source, and most readers instead delete the assertion.
        var openSet = Describe(typeof(PanelTaskBeforeTheFixDto), "Kind");
        Assert.Contains("PanelTaskBeforeTheFixDto.Kind", openSet, StringComparison.Ordinal);
        Assert.Contains("KindDisplayName", openSet, StringComparison.Ordinal);
        Assert.Contains("TaskKindDisplayNames", openSet, StringComparison.Ordinal);
        Assert.Contains("its set is open", openSet, StringComparison.Ordinal);

        var closedSet = Describe(typeof(ServiceStatusBeforeTheFixDto), "State");
        Assert.Contains("ServiceStatusBeforeTheFixDto.State", closedSet, StringComparison.Ordinal);
        Assert.Contains("AgentServiceState", closedSet, StringComparison.Ordinal);
        Assert.Contains("StateDisplayName", closedSet, StringComparison.Ordinal);
        Assert.Contains("DisplayNameExemptions", closedSet, StringComparison.Ordinal);
    }

    /// <summary>A dto that names its vocabulary is accepted.</summary>
    [Fact]
    public void A_dto_that_names_its_vocabulary_is_accepted()
    {
        // A gate mutated to refuse everything passes every test that only hands it broken input.
        Assert.Empty(DisplayNameLaw.UnnamedVocabularyMembers(typeof(PanelTaskAfterTheFixDto)));
        Assert.Empty(DisplayNameLaw.UnnamedVocabularyMembers(typeof(ServiceStatusAfterTheFixDto)));
    }

    /// <summary>Reads the member an exemption names, failing loudly when it has gone.</summary>
    /// <param name="exemption">The declared exemption.</param>
    /// <returns>The member it excuses.</returns>
    private static PropertyInfo MemberOfDeclaredType(DisplayNameExemption exemption)
    {
        var dto = DisplayNameLaw.OutwardDtoTypes().FirstOrDefault(type =>
        {
            return string.Equals(type.FullName, exemption.DtoTypeName, StringComparison.Ordinal);
        });

        Assert.True(dto is not null, $"{exemption.DtoTypeName} was not found among the outward DTOs.");
        var member = DisplayNameLaw.MemberOf(dto!, exemption.MemberName);
        Assert.True(member is not null, $"{exemption.DtoTypeName} has no member {exemption.MemberName}.");
        return member!;
    }

    /// <summary>Writes one violation the way the engineer who broke the law needs to read it.</summary>
    /// <param name="dto">The offending type.</param>
    /// <param name="memberName">The offending member.</param>
    /// <returns>The line that names the type, the member, and both ways out.</returns>
    private static string Describe(Type dto, string memberName)
    {
        var member = DisplayNameLaw.MemberOf(dto, memberName)!;
        var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
        var siblings = DisplayNameLaw.AcceptedSiblingNames(dto, member);
        var closed = type.IsEnum
            ? $"Because {type.Name} is a closed set, the alternative is to declare it in "
              + "DisplayNameExemptions with the locale key template the SPA words it by; the law "
              + "then checks that key exists for every member in en, ru and hy."
            : "It is a string, so its set is open — a bundle shipped today cannot hold a word for a "
              + "code invented tomorrow — and delegating it to the SPA is not available. The "
              + "backend names it or nobody does.";

        return $"  {dto.FullName}.{memberName} ({type.Name}) travels with no localized name beside "
            + $"it. Add a sibling string member — {string.Join(" or ", siblings)} — resolved for the "
            + "request's culture from the module's resx triple, the way TaskKindDisplayNames and "
            + $"BackupFailureDisplayNames do, and keep {memberName} on the wire for scripts and "
            + $"support tickets. {closed}";
    }
}
