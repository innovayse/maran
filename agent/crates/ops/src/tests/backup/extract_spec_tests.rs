//! What an extraction is told, and who it runs as.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_identity::ExtractIdentity;

use super::*;

/// The absolute compressor path every case here builds an argv with.
///
/// Absolute and made-up, for the reason the create side's own constant gives.
const COMPRESSOR: &str = "/fake/bin/compressor";

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// A spec for one part, over fixed paths.
fn spec(part: ArchivePart) -> ExtractSpec {
    ExtractSpec {
        artifact: PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        into: PathBuf::from("/var/lib/maran/scratch"),
        part,
    }
}

/// The identity an extraction runs as is derived from the member, so the dumps
/// are root's and the home is the account's and no call site can pair them the
/// other way round.
#[test]
fn the_identity_is_derived_from_the_member_and_cannot_be_set_beside_it() {
    assert_eq!(
        spec(ArchivePart::Manifest).identity(),
        ExtractIdentity::Root
    );
    assert_eq!(
        spec(ArchivePart::Databases).identity(),
        ExtractIdentity::Root
    );
    assert_eq!(
        spec(ArchivePart::Home { account: account() }).identity(),
        ExtractIdentity::Account(account())
    );
}

/// No extraction ever asks `tar` to honour absolute member names, which is the
/// belt behind the pre-scan's brace.
#[test]
fn no_extraction_argv_carries_absolute_names() {
    for part in [
        ArchivePart::Manifest,
        ArchivePart::Databases,
        ArchivePart::Home { account: account() },
    ] {
        let arguments = spec(part).arguments(COMPRESSOR);
        assert!(
            !arguments
                .iter()
                .any(|argument| argument == "-P" || argument == "--absolute-names"),
            "{arguments:?}"
        );
    }
}

/// The home's prefix comes off by stripping a component and never by a
/// transform — a transform rewrites relative symlink targets, which is the
/// silent corruption the create side had to disable a flag to avoid.
#[test]
fn the_home_prefix_is_stripped_and_never_transformed() {
    let arguments = spec(ArchivePart::Home { account: account() }).arguments(COMPRESSOR);

    assert!(
        arguments
            .iter()
            .any(|argument| argument == "--strip-components=1")
    );
    assert!(
        !arguments
            .iter()
            .any(|argument| argument.starts_with("--transform")),
        "{arguments:?}"
    );
    assert_eq!(arguments.last().map(String::as_str), Some("home"));
}

/// The root-side extractions keep the archive's own prefix, because the dumps
/// are looked up under `databases/` afterwards.
#[test]
fn the_root_side_extractions_keep_their_prefix() {
    let databases = spec(ArchivePart::Databases).arguments(COMPRESSOR);
    assert!(
        !databases
            .iter()
            .any(|argument| argument == "--strip-components=1")
    );
    assert_eq!(databases.last().map(String::as_str), Some("databases"));

    let manifest = spec(ArchivePart::Manifest).arguments(COMPRESSOR);
    assert_eq!(manifest.last().map(String::as_str), Some("manifest.json"));
}

/// Every extraction refuses the archive's own idea of ownership and mode.
#[test]
fn every_extraction_refuses_the_archives_ownership_and_mode() {
    for part in [
        ArchivePart::Manifest,
        ArchivePart::Databases,
        ArchivePart::Home { account: account() },
    ] {
        let arguments = spec(part).arguments(COMPRESSOR);
        assert!(
            arguments.iter().any(|a| a == "--no-same-owner"),
            "{arguments:?}"
        );
        assert!(
            arguments.iter().any(|a| a == "--no-same-permissions"),
            "{arguments:?}"
        );
        assert!(
            arguments.iter().any(|a| a == "--numeric-owner"),
            "{arguments:?}"
        );
    }
}

/// Every extraction names its decompressor by absolute path, so no `PATH` entry
/// decides which program reads a customer-supplied archive.
///
/// The restore half of the same defect the create side carried: `--gzip` had
/// `tar` fork `/bin/sh -c "gzip"` and resolve a bare name, as root, while
/// reading an artifact whose contents the panel does not trust. An extraction
/// still produces the right files either way, so the argv is the only place the
/// difference can be seen.
#[test]
fn every_extraction_names_its_decompressor_by_absolute_path() {
    for part in [
        ArchivePart::Manifest,
        ArchivePart::Databases,
        ArchivePart::Home { account: account() },
    ] {
        let arguments = spec(part).arguments(COMPRESSOR);

        for bare in ["--gzip", "-z", "--gunzip", "--ungzip"] {
            assert!(
                !arguments.iter().any(|argument| argument == bare),
                "{bare} resolves the decompressor through PATH: {arguments:?}"
            );
        }

        let named: Vec<&String> = arguments
            .iter()
            .filter(|argument| argument.starts_with("--use-compress-program="))
            .collect();
        assert_eq!(named.len(), 1, "{arguments:?}");

        let program = named[0]
            .strip_prefix("--use-compress-program=")
            .unwrap_or_default();
        assert!(
            program.starts_with('/'),
            "{program} is not an absolute path"
        );
        assert_eq!(program, COMPRESSOR);
    }
}

/// No extraction ever names the artifact on the archiver's command line: it
/// arrives as an open file on standard input, which is the only way an
/// extraction that runs AS THE ACCOUNT can read an artifact that is `0600`
/// inside a `0700` root-owned directory.
///
/// Asserted on the argv rather than on the outcome, because both shapes extract
/// the same files on a host where the reader happens to be root — the whole
/// defect this replaces was invisible everywhere except as the customer.
#[test]
fn no_extraction_names_the_artifact_as_a_path() {
    for part in [
        ArchivePart::Manifest,
        ArchivePart::Databases,
        ArchivePart::Home { account: account() },
    ] {
        let fixture = spec(part);
        let arguments = fixture.arguments(COMPRESSOR);

        let artifact = fixture.artifact.to_string_lossy().into_owned();
        assert!(
            !arguments.contains(&artifact),
            "the artifact is named on the command line: {arguments:?}"
        );

        let file = arguments
            .iter()
            .position(|argument| argument == "--file")
            .expect("every extraction names its archive");
        assert_eq!(
            arguments.get(file + 1).map(String::as_str),
            Some("-"),
            "{arguments:?}"
        );
    }
}

/// The archive is named exactly once, so there is no second, weaker way in for
/// a later edit to start relying on.
#[test]
fn the_archive_is_named_exactly_once() {
    for part in [
        ArchivePart::Manifest,
        ArchivePart::Databases,
        ArchivePart::Home { account: account() },
    ] {
        let arguments = spec(part).arguments(COMPRESSOR);
        assert_eq!(
            arguments
                .iter()
                .filter(|argument| *argument == "--file" || argument.starts_with("--file="))
                .count(),
            1,
            "{arguments:?}"
        );
        assert!(
            !arguments.iter().any(|argument| argument == "-f"),
            "{arguments:?}"
        );
    }
}
