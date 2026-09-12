using System.Globalization;
using System.Reflection;
using System.Resources;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Resources;

/// <summary>
/// What the operator actually reads, in all three languages the panel ships.
/// </summary>
/// <remarks>
/// The handler tests assert a refusal's CODE, because <c>Error</c> carries no message. These assert
/// the SENTENCE, because the sentence is where two promises live that a code cannot express: that no
/// customer-facing message names a path on the host, and that a refusal for a condition the operator
/// cannot retype their way out of does not tell them to correct their input.
/// </remarks>
public sealed class ErrorMessagesTests
{
    /// <summary>The three cultures this panel ships, neutral first.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>The absolute-path roots a customer-facing sentence may never name.</summary>
    /// <remarks>
    /// The list is the host's own top level rather than the two folders this module happens to read.
    /// <c>/etc/</c> and <c>/var/</c> alone were the paths the certificate and log refusals would have
    /// quoted, but the path a message here is far likelier to grow is <c>/home/</c>: every sentence
    /// in this file is about a login whose whole purpose is a jail under <c>/home/&lt;account&gt;/</c>,
    /// and it carries a NEIGHBOUR'S ACCOUNT NAME in it — so of the roots listed it is the only one
    /// whose disclosure is about somebody else. A sweep that could not see it was reporting on the
    /// two roots least likely to appear (rules/security.md item 8).
    /// </remarks>
    private static readonly string[] ForbiddenPathRoots =
        ["/etc/", "/var/", "/home/", "/root/", "/usr/", "/srv/", "/opt/", "/tmp/"];

    /// <summary>No error message in any locale carries a filesystem path.</summary>
    /// <remarks>
    /// The string a customer sees is a resx value rendered by culture, so this — not a member of
    /// <c>Error</c> — is where "no paths in a customer-facing message" is observable
    /// (rules/security.md item 8). All three locales, because a path pasted into the Russian
    /// translation alone would pass an English-only sweep. The per-locale <c>NotEmpty</c> is the
    /// vacuity guard on the axis that can go blind: a sweep over zero entries proves nothing, and a
    /// missing FILE throws in the reader rather than arriving here as an empty locale.
    /// </remarks>
    [Fact]
    public void No_error_message_in_any_locale_carries_a_filesystem_path()
    {
        var byLocale = ErrorMessageValues.ByLocale();
        Assert.Equal(3, byLocale.Count);

        foreach (var entries in byLocale)
        {
            Assert.NotEmpty(entries);
            Assert.All(entries, value =>
            {
                Assert.False(NamesAPath(value), value);
            });
        }
    }

    /// <summary>The sweep can see a planted path at every root it claims to guard.</summary>
    /// <remarks>
    /// <para>
    /// The positive control the sweep above owes, and it drives the sweep's OWN predicate rather
    /// than restating the comparison — a control that re-implements the check proves the control
    /// works, which is not the question. Without it, a reader that silently returned the wrong
    /// folder, or a <c>DoesNotContain</c> over text that could never have contained the substring,
    /// would certify the absence of a defect it had no way to detect.
    /// </para>
    /// <para>
    /// Every root is planted, not one: the sweep grew from two roots to eight, and a control that
    /// exercised only <c>/etc/</c> would have gone on passing if the other seven had been added to
    /// the list and dropped from the comparison.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_path_sweep_finds_a_planted_path_at_every_root_it_guards()
    {
        Assert.NotEmpty(ForbiddenPathRoots);

        Assert.All(ForbiddenPathRoots, root =>
        {
            var planted = ErrorMessageValues.ByLocale()[1]
                .Append($"Place the certificate in {root}maran/certificates and try again.")
                .ToList();

            Assert.Contains(planted, NamesAPath);
        });
    }

    /// <summary>The sweep sees the neighbour's home a jailed login's message would most likely name.</summary>
    /// <remarks>
    /// The root the old two-root sweep could not see, exercised on its own because it is the one
    /// whose disclosure is about a DIFFERENT customer: a sentence quoting <c>/home/bob/files</c>
    /// tells the reader that an account named <c>bob</c> exists on this server. The sweep above
    /// proves no shipped sentence contains it; this proves the sweep would notice if one did.
    /// </remarks>
    [Fact]
    public void The_path_sweep_finds_a_planted_neighbours_home()
    {
        Assert.True(NamesAPath("The login was created under /home/bob/files."));
        Assert.False(NamesAPath("The login was created under the account's own home."));
    }

    /// <summary>The missing-certificate refusal reads as a server condition in Russian.</summary>
    /// <remarks>
    /// A non-English locale on purpose: a loose locator or an English-only assertion has already let
    /// a wire constant through here, and a <c>ResourceManager</c> in a test process falls back to the
    /// neutral text when a satellite assembly is missing — so asserting the exact Russian sentence
    /// proves the satellite was built AND that the translation says what it should. The whole value
    /// is asserted, not a substring: a bound would pass on a sentence that had grown a "check your
    /// input" clause.
    /// </remarks>
    [Fact]
    public void The_missing_certificate_refusal_reads_as_a_server_condition_in_russian()
    {
        Assert.Equal(
            "FTPS не запустится, пока для этого имени хоста нет TLS-сертификата. Введённые вами "
            + "данные верны: выпустите или установите сертификат в разделе SSL, затем снова "
            + "включите FTPS.",
            Read(ErrorCodes.FtpsCertificateMissing, "ru"));
    }

    /// <summary>The missing-certificate refusal reads as a server condition in Armenian.</summary>
    [Fact]
    public void The_missing_certificate_refusal_reads_as_a_server_condition_in_armenian()
    {
        Assert.Equal(
            "FTPS-ը չի գործարկվի, քանի դեռ հոսթի այս անվան համար TLS վկայագիր չկա։ Ձեր "
            + "մուտքագրածում սխալ բան չկա. թողարկեք կամ տեղադրեք վկայագիր SSL բաժնում, ապա նորից "
            + "միացրեք FTPS-ը։",
            Read(ErrorCodes.FtpsCertificateMissing, "hy"));
    }

    /// <summary>The missing-certificate refusal names the SSL section in every language.</summary>
    /// <remarks>
    /// The one thing an operator can act on. A refusal that stated the condition and no remedy would
    /// leave them on a form whose every field is already correct — which is the defect this task
    /// exists to avoid, in the shape it took the last time: a refusal reading "correct them and try
    /// again" for a condition retyping could never fix.
    /// </remarks>
    [Fact]
    public void The_missing_certificate_refusal_names_the_ssl_section_in_every_language()
    {
        Assert.All(Cultures, culture =>
        {
            Assert.Contains(
                "SSL",
                Read(ErrorCodes.FtpsCertificateMissing, culture),
                StringComparison.Ordinal);
        });
    }

    /// <summary>The refusal for a plan that filled up mid-creation says the input was not wrong.</summary>
    /// <remarks>
    /// <para>
    /// The calibration this refusal has to match, and the reason it is a separate sentence from
    /// <c>FtpUserLimitReached</c> at all. A customer who lost a race they could not see must not be
    /// told to correct something: everything they typed was valid, the login was made and then removed
    /// again, and the one action that helps is deleting a login they no longer need. The clause is
    /// asserted in all three languages because a translation that dropped it would leave exactly the
    /// "correct your input and retry" reading this refusal exists to avoid.
    /// </para>
    /// <para>
    /// The clauses are exact per locale rather than one shared substring, since there is no language
    /// in which the three sentences share a word. Each is the same promise the missing-certificate
    /// refusal makes, in the same words that refusal already uses in that locale.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_concurrent_limit_refusal_says_the_input_was_not_wrong_in_every_language()
    {
        var clauses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "Nothing you entered is wrong.",
            ["ru"] = "Введённые вами данные верны.",
            ["hy"] = "Ձեր մուտքագրածում սխալ բան չկա։",
        };

        Assert.Equal(Cultures.Length, clauses.Count);

        Assert.All(Cultures, culture =>
        {
            Assert.Contains(
                clauses[culture],
                Read(ErrorCodes.FtpUserLimitReachedConcurrently, culture),
                StringComparison.Ordinal);
        });
    }

    /// <summary>The clause sweep above would notice a refusal that had lost it.</summary>
    /// <remarks>
    /// The positive control that sweep owes. <c>FtpUserLimitReached</c> is the neighbouring refusal —
    /// a plan that was already full, where the customer's input is equally blameless but nothing was
    /// created and nothing had to be undone — and it carries no such clause in any locale. If the
    /// assertion above could pass on any of this module's sentences, this would fail.
    /// </remarks>
    [Fact]
    public void The_clause_sweep_does_not_match_the_plain_limit_refusal()
    {
        Assert.DoesNotContain(
            "Nothing you entered is wrong.",
            Read(ErrorCodes.FtpUserLimitReached, "en"),
            StringComparison.Ordinal);
    }

    /// <summary>Every code this module raises has an entry in all three locales.</summary>
    /// <remarks>
    /// This is what makes the spelled constants in <see cref="ErrorCodes"/> safe. A code with no
    /// entry is not a build error and not a runtime error: <c>ResourceManager</c> answers with the
    /// key itself, so the operator reads <c>FtpsCertificateMissing</c> where a sentence should be.
    /// Checking all three files is the half <c>nameof</c> never could — it proved a key existed in
    /// the NEUTRAL resx and said nothing about the two translations.
    /// </remarks>
    [Fact]
    public void Every_code_this_module_raises_has_an_entry_in_all_three_locales()
    {
        var codes = DeclaredCodes();
        Assert.NotEmpty(codes);

        var byLocale = ErrorMessageValues.KeysByLocale();
        Assert.Equal(3, byLocale.Count);

        Assert.All(byLocale, keys =>
        {
            Assert.NotEmpty(keys);
            Assert.All(codes, code =>
            {
                Assert.Contains(code, keys);
            });
        });
    }

    /// <summary>The reflected code list really reads the type, and it reads all of it.</summary>
    /// <remarks>
    /// The positive control <see cref="DeclaredCodes"/> owes. A reflection query with the wrong
    /// binding flags returns an EMPTY list, and an empty list makes
    /// <see cref="Every_code_this_module_raises_has_an_entry_in_all_three_locales"/> pass over
    /// nothing at all — the exact vacuity that certifies the absence of a defect it could not see.
    /// So this asserts both ends: a named constant the module actually raises is present, and the
    /// count equals the number of public string constants the type declares.
    /// </remarks>
    [Fact]
    public void The_declared_code_sweep_reads_every_constant_on_the_type()
    {
        var codes = DeclaredCodes();

        Assert.Contains(ErrorCodes.FtpsCertificateMissing, codes);
        Assert.Contains(ErrorCodes.AccountNotFound, codes);
        Assert.Equal(
            typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Length,
            codes.Count);
    }

    /// <summary>
    /// Every translated entry is a translation: not the key, not the English, not a neighbour's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property with teeth for a locale file, and it is chosen against the two ways a
    /// translation actually goes wrong here. A key left untranslated ships the identifier
    /// <c>FtpUserNotFound</c> to a customer; a key copied but not translated ships English into a
    /// Russian screen; and the realistic third — the one a spell-checker and a key-parity sweep both
    /// miss — is a NEIGHBOURING entry pasted into the wrong slot, which reads as fluent Armenian and
    /// answers the wrong question. So each translated value must differ from its key, from the
    /// neutral English for the same key, and from every other value in its own file.
    /// </para>
    /// <para>
    /// Asserting exact sentences instead would turn every copy edit into a failing test and teach
    /// the next contributor to update assertions reflexively; asserting non-emptiness would observe
    /// almost nothing, since all three failure shapes above are non-empty. The two exact-sentence
    /// tests in this file are the deliberate exception, and each exists for one clause that carries
    /// a promise.
    /// </para>
    /// <para>
    /// The keys and values are zipped by POSITION, which is sound because
    /// <c>ErrorMessageValues</c> reads both out of the same <c>&lt;data&gt;</c> sequence of the same
    /// document. The per-locale <c>NotEmpty</c> is the vacuity guard on the axis that can go blind,
    /// and <see cref="A_neighbours_text_pasted_into_another_slot_is_detected"/> is its positive
    /// control.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_translated_entry_is_a_translation_and_not_a_neighbours_text()
    {
        var keys = ErrorMessageValues.KeysByLocale();
        var values = ErrorMessageValues.ByLocale();
        Assert.Equal(3, values.Count);

        var neutral = Pairs(keys[0], values[0]);

        for (var locale = 1; locale < values.Count; locale++)
        {
            var translated = Pairs(keys[locale], values[locale]);
            Assert.NotEmpty(translated);
            Assert.Equal(translated.Count, translated.Values.Distinct(StringComparer.Ordinal).Count());

            foreach (var entry in translated)
            {
                Assert.NotEqual(entry.Key, entry.Value);
                Assert.NotEqual(neutral[entry.Key], entry.Value);
            }
        }
    }

    /// <summary>The distinctness check sees a neighbour's sentence pasted into another slot.</summary>
    /// <remarks>
    /// The positive control the check above owes. It plants exactly the defect the check hunts — one
    /// entry's text sitting in another entry's slot — and asserts the detector finds it. Without
    /// this, a table that happened to contain no duplicates for an unrelated reason would certify
    /// the absence of a defect the assertion could never have seen.
    /// </remarks>
    [Fact]
    public void A_neighbours_text_pasted_into_another_slot_is_detected()
    {
        var values = ErrorMessageValues.ByLocale()[2].ToList();
        values[0] = values[1];

        Assert.NotEqual(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The failed-creation refusal does not promise the host was cleaned up.</summary>
    /// <remarks>
    /// <para>
    /// An exact sentence, in all three languages, because the clause that matters is a PROMISE about
    /// the host rather than a phrasing. The compensating delete in
    /// <c>CreateFtpUserCommandHandler.CompensateAsync</c> is best effort: when it fails it logs and
    /// says nothing to the customer, so a message reading "nothing was left behind" states something
    /// the panel does not know. Worse, it is wrong in exactly the case the customer will meet — the
    /// retry it advises is then refused as a taken name, with no explanation for why a name they
    /// have never successfully used is taken.
    /// </para>
    /// <para>
    /// A substring bound would not do: "please try again" appears in the accurate sentence and in
    /// the overclaiming one alike, so it cannot tell them apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_failed_creation_refusal_does_not_promise_the_host_was_cleaned()
    {
        Assert.Equal(
            "This FTPS user could not be created and no password was issued. Please try again. If "
            + "the name is then reported as already taken, contact support: a leftover login may "
            + "have to be removed.",
            Read(ErrorCodes.FtpUserProvisioningFailed, "en"));

        Assert.Equal(
            "Не удалось создать пользователя FTPS, пароль не выдан. Повторите попытку. Если имя "
            + "окажется занято, обратитесь в поддержку: возможно, потребуется удалить оставшегося "
            + "пользователя.",
            Read(ErrorCodes.FtpUserProvisioningFailed, "ru"));

        Assert.Equal(
            "Չհաջողվեց ստեղծել FTPS օգտվողը, և գաղտնաբառ չի տրվել։ Կրկնեք փորձը։ Եթե անունն "
            + "այնուհետև զբաղված նշվի, դիմեք աջակցման ծառայությանը․ հնարավոր է՝ անհրաժեշտ լինի "
            + "հեռացնել մնացած օգտվողին։",
            Read(ErrorCodes.FtpUserProvisioningFailed, "hy"));
    }

    /// <summary>Every code <see cref="ErrorCodes"/> declares, read off the type rather than listed.</summary>
    /// <returns>The value of every public string constant on <see cref="ErrorCodes"/>.</returns>
    /// <remarks>
    /// Read by reflection because the alternative — a literal array in the assertion — is a second
    /// list that has to be kept equal to the first, and the failure it produces is silent in the
    /// dangerous direction: a code added to <see cref="ErrorCodes"/> and to the handler, but not to
    /// the array, ships without its two translations and the sweep reports nothing. The caller's
    /// <c>NotEmpty</c> is the vacuity guard on the axis that can go blind, and
    /// <see cref="The_declared_code_sweep_reads_every_constant_on_the_type"/> is its positive
    /// control.
    /// </remarks>
    private static List<string> DeclaredCodes()
    {
        return typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field =>
            {
                return field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string);
            })
            .Select(field =>
            {
                return (string)field.GetRawConstantValue()!;
            })
            .ToList();
    }

    /// <summary>Whether a sentence names an absolute path on the host.</summary>
    /// <param name="value">One message as a customer would read it.</param>
    /// <returns>True when the text contains any of <see cref="ForbiddenPathRoots"/>.</returns>
    /// <remarks>
    /// One predicate, shared by the sweep and by its positive controls, so that the thing proved
    /// observable is the thing the sweep runs.
    /// </remarks>
    private static bool NamesAPath(string value)
    {
        return ForbiddenPathRoots.Any(root =>
        {
            return value.Contains(root, StringComparison.Ordinal);
        });
    }

    /// <summary>Zips one locale's keys and values into a lookup.</summary>
    /// <param name="keys">The locale's keys, in document order.</param>
    /// <param name="values">The same locale's values, in the same document order.</param>
    /// <returns>The locale's entries by key.</returns>
    private static Dictionary<string, string> Pairs(IReadOnlyList<string> keys, IReadOnlyList<string> values)
    {
        Assert.Equal(keys.Count, values.Count);

        return keys.Zip(values).ToDictionary(
            pair =>
            {
                return pair.First;
            },
            pair =>
            {
                return pair.Second;
            },
            StringComparer.Ordinal);
    }

    /// <summary>Reads one error sentence in one culture, through the same path a request would.</summary>
    /// <param name="key">The resource key, which is also the error code.</param>
    /// <param name="culture">The culture name, such as <c>ru</c>.</param>
    /// <returns>The sentence as that culture's reader would see it.</returns>
    private static string Read(string key, string culture)
    {
        var manager = new ResourceManager(
            "Maran.Modules.Ftp.Resources.ErrorMessages", typeof(FtpModule).Assembly);

        return manager.GetString(key, new CultureInfo(culture)) ?? string.Empty;
    }
}
