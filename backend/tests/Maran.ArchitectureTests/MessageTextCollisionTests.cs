namespace Maran.ArchitectureTests;

/// <summary>
/// One message key must mean one sentence, in every language.
/// </summary>
/// <remarks>
/// <para>
/// Every module's resources feed ONE pool (<see cref="ManifestUniquenessTests"/>), and
/// <c>ResxErrorTextProvider</c> resolves a key by trying each module's table in REGISTRATION ORDER
/// and taking the first hit. Several modules deliberately declare the same key — a module may not
/// read another module's resources, so each ships its own copy of <c>AccountNotFound</c> — and that
/// duplication is correct. What is not correct is two copies saying two different things: then the
/// text a customer reads is decided by the order modules happen to be registered in, nobody chose
/// it, and a refactor that reorders registration silently changes error messages in every language.
/// </para>
/// <para>
/// The runtime already warns about this, which is how it was found: a working server logged, on
/// every boot, that <c>AccountNotFound</c> had different text in eight modules (issue #76). A
/// warning in a log nobody reads is not a guard — it fires after the build that introduced the
/// divergence has shipped — so this law fails the build instead.
/// </para>
/// <para>
/// UNOBSERVED HERE: this law compares the text modules wrote down. It cannot tell a good sentence
/// from a bad one, nor a correct translation from a fluent wrong one, and a key that two modules
/// agree on but both word badly passes. It also reports on the pool as a whole, exactly as broad as
/// the provider's own lookup — not on one module's file.
/// </para>
/// </remarks>
public sealed class MessageTextCollisionTests
{
    /// <summary>No key is claimed with two different texts, in the neutral files or any translation.</summary>
    /// <param name="culture">The culture under test, or <c>null</c> for the neutral English files.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("ru")]
    [InlineData("hy")]
    public void A_message_key_has_one_text_across_every_module(string? culture)
    {
        var declarations = ResourceTriples.ReadAll(culture);

        // Asserted as the whole report rather than one key at a time: an author who unified two of
        // three languages should see the third in the same failure, not on the next run.
        var collisions = declarations
            .Where(entry =>
            {
                return entry.Value.Distinct(StringComparer.Ordinal).Count() > 1;
            })
            .OrderBy(entry =>
            {
                return entry.Key;
            }, StringComparer.Ordinal)
            .Select(entry =>
            {
                var texts = entry.Value
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(text =>
                    {
                        return $"    {text}";
                    });

                return $"{entry.Key}:{Environment.NewLine}{string.Join(Environment.NewLine, texts)}";
            })
            .ToList();

        Assert.True(
            collisions.Count == 0,
            $"These message keys are declared with more than one text for culture "
            + $"'{culture ?? "neutral"}', so registration order decides which one a customer reads. "
            + $"Make every copy of a key identical, or give the modules different keys:"
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, collisions)}");
    }

    /// <summary>
    /// The control the law above needs: that it is reading anything at all.
    /// </summary>
    /// <remarks>
    /// Without this, a <c>ReadAll</c> that silently returned nothing — a moved folder, a renamed
    /// resource family, a changed build layout — would make the law above pass by having no keys to
    /// judge, and it would go on passing forever. The number is a floor, not a count: it must not
    /// need editing every time a module adds a message.
    /// </remarks>
    [Fact]
    public void The_law_above_is_reading_the_resource_files_it_judges()
    {
        var declarations = ResourceTriples.ReadAll(culture: null);

        Assert.True(
            declarations.Count > 50,
            $"Only {declarations.Count} message keys were read from backend/src, so the collision law "
            + "above is reporting on almost nothing.");

        // And that duplication across modules is actually visible here: a ReadAll that merged copies
        // down to one value per key could never see a collision, and the law would be vacuous.
        Assert.Contains(
            declarations,
            entry =>
            {
                return entry.Value.Count > 1;
            });
    }
}
