using Maran.Modules.Licensing.Commands.InstallLicence;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Commands.InstallLicence;

/// <summary>
/// Covers <see cref="InstallLicenceCommandHandler"/>'s own guarantees, per
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c>: verify before
/// persist, each rejection distinguishable, and every outcome journalled.
/// </summary>
public sealed class InstallLicenceCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset FixedReferenceInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "install-licence-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    private string LicenceFilePath
    {
        get
        {
            return Path.Combine(_directory, "licence.json");
        }
    }

    /// <summary>Builds a handler over the real verifier/writer/reader, with the given writer overridden.</summary>
    private (InstallLicenceCommandHandler Handler, RecordingAuditWriter Journal) BuildHandler(ILicenceWriter? writer = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new LicenceStorageOptions { LicenceFilePath = LicenceFilePath });
        var rawTextSource = new FileLicenceRawTextSource(options);
        var verifier = new LicenceVerifier(
            new Ed25519LicenceSignatureVerifier(),
            new FixedClock(FixedReferenceInstant),
            new FixedServerIdentitySource());
        var installWriter = writer ?? new FileLicenceWriter(options);
        var auditWriter = new RecordingAuditWriter();
        var journal = new LicensingAuditJournal(auditWriter, new FakeCurrentUser(isAdmin: true));

        var handler = new InstallLicenceCommandHandler(
            rawTextSource,
            verifier,
            installWriter,
            new LicenceInstallLock(),
            LicensingTestContext.StatusText(),
            journal);

        return (handler, auditWriter);
    }

    private static InstallLicenceCommand CommandFor(string rawLicenceText)
    {
        return new InstallLicenceCommand(rawLicenceText, IpAddress: "127.0.0.1", UserAgent: "test-agent");
    }

    // ---- Verify-then-persist: the mandatory "persist before verify" mutant --------------------

    /// <summary>
    /// A rejected upload leaves the previous licence exactly as it was. Kills the "persist before
    /// verify" mutant: if the handler wrote before checking the verifier's result, the malformed
    /// text below would have overwritten the valid licence already on disk.
    /// </summary>
    [Fact]
    public async Task A_rejected_upload_leaves_the_previous_licence_untouched()
    {
        var (handler, _) = BuildHandler();
        var validEnvelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-original");

        var installed = await handler.HandleAsync(CommandFor(validEnvelope), CancellationToken.None);
        Assert.True(installed.IsSuccess);

        var rejected = await handler.HandleAsync(CommandFor("this is not a licence envelope"), CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("LicenceInstallMalformed", rejected.Error!.Code);
        Assert.Equal(validEnvelope, await File.ReadAllTextAsync(LicenceFilePath));
    }

    /// <summary>The inverse control: a signature that does not verify is also refused and changes nothing.</summary>
    [Fact]
    public async Task An_upload_signed_with_the_wrong_key_is_refused_and_leaves_the_previous_licence_untouched()
    {
        var (handler, _) = BuildHandler();
        var validEnvelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-original");
        await handler.HandleAsync(CommandFor(validEnvelope), CancellationToken.None);

        var forged = LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, id: "lic-forged");
        var rejected = await handler.HandleAsync(CommandFor(forged), CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("LicenceInstallSignatureInvalid", rejected.Error!.Code);
        Assert.Equal(validEnvelope, await File.ReadAllTextAsync(LicenceFilePath));
    }

    /// <summary>A valid upload with nothing previously installed is accepted and written.</summary>
    [Fact]
    public async Task A_valid_upload_with_nothing_previously_installed_is_accepted_and_written()
    {
        var (handler, journal) = BuildHandler();
        var envelope = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-first");

        var result = await handler.HandleAsync(CommandFor(envelope), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Valid", result.Value!.State);
        Assert.Equal("lic-first", result.Value.LicenceId);
        Assert.Equal(envelope, await File.ReadAllTextAsync(LicenceFilePath));

        // The success sentence says what decides whether the licence is tied to this server, rather
        // than asserting it is not. Binding exists now — the old wording ("does not bind ... the same
        // file would also verify on any other installation") became false the day the `server` claim
        // was honoured, and it was false in the direction that favours a copied licence. This licence
        // names no server, so what the operator must be told is the CONDITION, not a verdict.
        Assert.Contains("named server", result.Value.Sentence, StringComparison.OrdinalIgnoreCase);

        var success = Assert.Single(journal.Entries);
        Assert.True(success.Succeeded);
        Assert.Equal(AuditActions.LicenceInstalled, success.Action);
        Assert.DoesNotContain("signature", success.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A replace journals the PREVIOUS licence's id alongside the new one.</summary>
    [Fact]
    public async Task Replacing_a_licence_journals_the_previous_licences_id()
    {
        var (handler, journal) = BuildHandler();
        await handler.HandleAsync(CommandFor(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-old")), CancellationToken.None);

        var replaceResult = await handler.HandleAsync(
            CommandFor(LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-new")), CancellationToken.None);

        Assert.True(replaceResult.IsSuccess);
        var replaceEntry = journal.Entries[1];
        Assert.Contains("id=lic-new", replaceEntry.Subject, StringComparison.Ordinal);
        Assert.Contains("previousId=lic-old", replaceEntry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Every rejection reason maps to its own distinguishable, closed-set error code.</summary>
    [Fact]
    public async Task An_expired_licence_is_refused_with_its_own_distinguishable_code()
    {
        var (handler, _) = BuildHandler();
        var expired = LicenceEnvelopeBuilder.Build(
            TestKeys.RealPrivateKeyHex, expiry: FixedReferenceInstant.AddDays(-1));

        var result = await handler.HandleAsync(CommandFor(expired), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("LicenceInstallExpired", result.Error!.Code);
    }

    // ---- The mandatory "atomic rename replaced by a non-atomic write" mutant ------------------

    /// <summary>
    /// The previous licence survives a write that fails. Kills the "atomic rename replaced by a
    /// non-atomic write" mutant: a non-atomic overwrite-in-place could leave the target corrupted or
    /// replaced by the time <see cref="ILicenceWriter.InstallAsync"/> throws; this handler's call to
    /// the writer is the only place the target is ever touched, so a writer that fails before
    /// completing its own atomic swap (as the real <see cref="FileLicenceWriter"/> does, per
    /// <c>AtomicFileWriterTests</c>) can never leave a partial result — proven here at the handler
    /// boundary with a writer that fails outright.
    /// </summary>
    [Fact]
    public async Task The_previous_licence_survives_a_failed_write()
    {
        var (installOk, _) = BuildHandler();
        var previous = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-safe");
        await installOk.HandleAsync(CommandFor(previous), CancellationToken.None);

        var (installFails, journal) = BuildHandler(new ThrowingLicenceWriter());
        var next = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-would-replace");

        await Assert.ThrowsAsync<IOException>(() =>
        {
            return installFails.HandleAsync(CommandFor(next), CancellationToken.None);
        });

        Assert.Equal(previous, await File.ReadAllTextAsync(LicenceFilePath));
        // No success entry was ever written for the failed attempt.
        Assert.DoesNotContain(journal.Entries, entry =>
        {
            return entry.Succeeded;
        });
    }

    /// <summary>
    /// A licence that verified and then could not be written leaves a FAILURE entry naming the stage.
    /// </summary>
    /// <remarks>
    /// Found by driving the running panel, not by reading the code: a dev API cannot write
    /// /var/lib/maran, so a good licence produced a 500 and the audit trail showed nothing at all
    /// between the status read and the operator's confusion. Verification had already succeeded, so
    /// this is the one case the journal must never lose — somebody presented a valid licence and the
    /// panel failed to keep it.
    ///
    /// Note what the sibling test above could NOT see: it asserts only that no SUCCESS entry exists,
    /// which was true both before and after the fix. A check that cannot observe the defect it would
    /// be cited for is not a guard, and this one is written as the pair it needed.
    /// </remarks>
    [Fact]
    public async Task A_licence_that_verified_but_could_not_be_written_is_journalled_as_a_failure()
    {
        var (installFails, journal) = BuildHandler(new ThrowingLicenceWriter());
        var licence = LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, id: "lic-unpersisted");

        await Assert.ThrowsAsync<IOException>(() =>
        {
            return installFails.HandleAsync(CommandFor(licence), CancellationToken.None);
        });

        var entry = Assert.Single(journal.Entries);
        Assert.False(entry.Succeeded);
        // The subject names the stage rather than the exception: a filesystem path or an exception
        // message in an audit subject would put host detail into a journal shown in a browser.
        Assert.Equal("action=Install;status=Valid;persisted=false", entry.Subject);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
