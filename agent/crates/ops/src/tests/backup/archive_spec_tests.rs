//! What `tar` is told, and the two flags whose absence would leak a host.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use super::*;

/// The absolute compressor path every case here builds an argv with.
///
/// Absolute and made-up: absolute is the property under test, and the real
/// `/usr/bin/gzip` is the distro adapter's answer, not this crate's.
const COMPRESSOR: &str = "/fake/bin/compressor";

/// A spec over ordinary paths.
fn spec() -> ArchiveSpec {
    ArchiveSpec {
        home: PathBuf::from("/home/alice"),
        scratch: PathBuf::from("/run/maran/scratch/backup/an-id"),
        artifact: PathBuf::from("/var/backups/maran/alice/an-id.tar.gz.partial"),
    }
}

/// The archive is taken with `--one-file-system` and never with `-h`.
#[test]
fn the_archive_argv_contains_one_file_system_and_never_dereference() {
    let arguments = spec().arguments(COMPRESSOR);

    assert!(
        arguments
            .iter()
            .any(|argument| argument == "--one-file-system")
    );
    assert!(!arguments.iter().any(|argument| argument == "-h"));
    assert!(!arguments.iter().any(|argument| argument == "--dereference"));
}

/// A symlink in the home is stored as a link: nothing turns dereferencing on,
/// and the rename that builds the `home/` prefix does not touch link targets.
#[test]
fn a_symlink_in_the_home_is_archived_as_a_link_and_not_followed() {
    let arguments = spec().arguments(COMPRESSOR);

    assert!(!arguments.iter().any(|argument| argument == "-h"));
    assert!(!arguments.iter().any(|argument| argument == "--dereference"));

    let transform = arguments
        .iter()
        .position(|argument| argument == "--transform")
        .map(|index| arguments[index + 1].clone())
        .unwrap_or_default();
    assert!(
        transform.ends_with('S'),
        "the rename must carry the S flag, or tar rewrites relative symlink \
         targets: {transform}"
    );
}

/// The members land under the archive's three fixed prefixes.
#[test]
fn the_members_are_the_home_the_dumps_and_the_manifest() {
    let arguments = spec().arguments(COMPRESSOR);

    assert_eq!(arguments.last().map(String::as_str), Some("manifest.json"));
    assert!(arguments.iter().any(|argument| argument == "databases"));
    assert!(arguments.iter().any(|argument| argument == "."));
}

/// The archiver writes the `.partial`, never the published name.
#[test]
fn the_archiver_is_told_to_write_the_partial_name() {
    let arguments = spec().arguments(COMPRESSOR);
    let file = arguments
        .iter()
        .position(|argument| argument == "--file")
        .map(|index| arguments[index + 1].clone())
        .unwrap_or_default();

    assert!(file.ends_with(".tar.gz.partial"), "{file}");
}

/// The rename expression names no account, so a scratch member can never match
/// it — an account called `data` once turned `databases/` into `homebases/`.
#[test]
fn the_rename_expression_cannot_match_a_scratch_member() {
    let arguments = spec().arguments(COMPRESSOR);
    let transform = arguments
        .iter()
        .position(|argument| argument == "--transform")
        .map(|index| arguments[index + 1].clone())
        .unwrap_or_default();

    assert!(!transform.contains("alice"), "{transform}");
    assert!(!transform.contains("databases"), "{transform}");
    assert!(transform.starts_with(r"s|^\."), "{transform}");
}

/// Compression is gzip, and ownership is stored numerically.
#[test]
fn the_archive_is_gzip_with_numeric_owners() {
    let arguments = spec().arguments(COMPRESSOR);

    assert!(
        arguments
            .iter()
            .any(|argument| argument == &format!("--use-compress-program={COMPRESSOR}"))
    );
    assert!(
        arguments
            .iter()
            .any(|argument| argument == "--numeric-owner")
    );
}

/// The compressor is named by absolute path, and `PATH` is never asked.
///
/// This is the assertion the previous shape could not make. `--gzip` produced a
/// perfectly good archive, so every test that checked the artifact passed while
/// `tar` forked `/bin/sh -c "gzip"` and let `PATH` — whose first entries under
/// systemd are `/usr/local/sbin:/usr/local/bin`, where this product installs —
/// choose which program compressed a root daemon's customer data. Nothing about
/// the OUTPUT distinguishes the two, so the argv is the only place the
/// difference is observable, and this test looks exactly there.
#[test]
fn the_compressor_is_named_by_absolute_path_and_never_resolved_through_path() {
    let arguments = spec().arguments(COMPRESSOR);

    for bare in ["--gzip", "-z", "--gunzip", "--ungzip"] {
        assert!(
            !arguments.iter().any(|argument| argument == bare),
            "{bare} makes tar resolve the compressor through PATH: {arguments:?}"
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
