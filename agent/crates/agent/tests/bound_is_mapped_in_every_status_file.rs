//! Every area error that can carry a stopped program has an rpc status arm
//! that names it.
//!
//! The per-area status tests pin what each arm answers. What none of them can
//! see is an area that gained a `Bound` variant and no arm at all: the status
//! mapping ends in a wildcard that also answers `SystemFailure`, so the
//! omission changes no behaviour a behavioural test could observe. This is a
//! census over the source of both crates, so a NEW area error is covered
//! without anyone extending a list.
//!
//! UNOBSERVED HERE: what an arm answers (the per-area `*_status_tests.rs` do
//! that) and whether the arm sits ahead of the wildcard (the same files).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::path::{Path, PathBuf};

/// The line every area error uses to carry a stopped program.
const BOUND_VARIANT_LINE: &str = "Bound(#[from] BoundExceeded),";

/// Area errors that carry a `Bound` and are deliberately mapped by no status
/// file, each with the reason. The census fails when one of these stops being
/// a bound-carrying error, so an entry cannot outlive its reason.
const UNMAPPED_BY_DESIGN: [(&str, &str); 1] = [(
    "SafeWriteError",
    "the config-write protocol is not an rpc area: every area that calls it wraps its bound in \
     its own variant (SitesOpError, PhpOpError, SslOpError, SftpError, ...), and those are mapped",
)];

/// Every `*.rs` file below `root`, recursively.
fn rust_files_below(root: &Path) -> Vec<PathBuf> {
    let mut found = Vec::new();
    let mut pending = vec![root.to_path_buf()];
    while let Some(directory) = pending.pop() {
        for entry in fs::read_dir(&directory).unwrap() {
            let path = entry.unwrap().path();
            if path.is_dir() {
                pending.push(path);
            } else if path.extension().is_some_and(|extension| extension == "rs") {
                found.push(path);
            }
        }
    }
    found.sort();
    found
}

/// The name of the enum a `*_error.rs` source declares, when it declares a
/// `Bound` variant that wraps the core's `BoundExceeded`.
fn enum_carrying_a_bound(source: &str) -> Option<String> {
    let has_bound = source.lines().any(|line| line.trim() == BOUND_VARIANT_LINE);
    if !has_bound {
        return None;
    }
    source.lines().find_map(|line| {
        let declaration = line.trim().strip_prefix("pub enum ")?;
        let name: String = declaration
            .chars()
            .take_while(|character| character.is_alphanumeric())
            .collect();
        (!name.is_empty()).then_some(name)
    })
}

/// Whether `status_source` has a match arm that names `enum_name`'s `Bound`.
///
/// The arm must be UNGUARDED: a pattern followed by an `if` guard that can be
/// false falls through to the wildcard, so it names the bound without mapping
/// it. The pattern is the text between the variant and the first `=>` on the
/// line, and a guard there (` if `) disqualifies the arm.
fn names_the_bound_arm(status_source: &str, enum_name: &str) -> bool {
    let arm = format!("{enum_name}::Bound(");
    status_source
        .lines()
        .map(str::trim_start)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| line.starts_with(&arm))
        .any(|line| {
            line.split_once("=>")
                .is_some_and(|(pattern, _)| !pattern.contains(" if "))
        })
}

/// The path of a sibling crate's `src` folder.
fn sibling_source(crate_folder: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join(crate_folder)
        .join("src")
}

/// Every area error in `ops` that carries a `Bound`, with the file it is in.
fn bound_carrying_errors() -> Vec<(String, PathBuf)> {
    rust_files_below(&sibling_source("ops"))
        .into_iter()
        .filter(|path| {
            path.file_name()
                .and_then(|name| name.to_str())
                .is_some_and(|name| name.ends_with("_error.rs"))
        })
        .filter(|path| !path.components().any(|part| part.as_os_str() == "tests"))
        .filter_map(|path| {
            let source = fs::read_to_string(&path).unwrap();
            enum_carrying_a_bound(&source).map(|name| (name, path))
        })
        .collect()
}

/// The concatenated source of every `*_status.rs` in the agent's services.
fn status_sources() -> Vec<String> {
    rust_files_below(&sibling_source("agent").join("services"))
        .into_iter()
        .filter(|path| {
            path.file_name()
                .and_then(|name| name.to_str())
                .is_some_and(|name| name.ends_with("_status.rs"))
        })
        .map(|path| fs::read_to_string(path).unwrap())
        .collect()
}

/// Every area error that carries a bound is named by an arm in a status file,
/// except the ones whose absence is documented.
#[test]
fn every_area_error_that_carries_a_bound_is_named_by_a_status_arm() {
    let errors = bound_carrying_errors();
    let statuses = status_sources();

    // Positive control: the census can see the errors and the status files it
    // is measured against, including one error that is known to carry a bound.
    assert!(
        errors.iter().any(|(name, _)| name == "SitesOpError"),
        "the census did not find SitesOpError among {errors:?}"
    );
    assert!(
        errors.len() >= 13,
        "the census found only {} bound-carrying errors",
        errors.len()
    );
    assert!(
        statuses.len() >= 12,
        "only {} status files found",
        statuses.len()
    );

    let unmapped: Vec<&String> = errors
        .iter()
        .map(|(name, _)| name)
        .filter(|name| !UNMAPPED_BY_DESIGN.iter().any(|(exempt, _)| exempt == name))
        .filter(|name| {
            !statuses
                .iter()
                .any(|source| names_the_bound_arm(source, name))
        })
        .collect();
    assert!(
        unmapped.is_empty(),
        "these area errors carry a Bound and no status file names it: {unmapped:?}"
    );
}

/// An exemption for an error that no longer carries a bound, or that a status
/// file now maps, fails: an entry cannot outlive its reason.
#[test]
fn every_exemption_names_an_error_that_still_needs_it() {
    let errors = bound_carrying_errors();
    let statuses = status_sources();

    for (exempt, reason) in UNMAPPED_BY_DESIGN {
        assert!(!reason.is_empty(), "{exempt} is exempt without a reason");
        assert!(
            errors.iter().any(|(name, _)| name == exempt),
            "{exempt} is exempt but no longer carries a Bound"
        );
        assert!(
            !statuses
                .iter()
                .any(|source| names_the_bound_arm(source, exempt)),
            "{exempt} is exempt but a status file maps it now"
        );
    }
}

/// The detector accepts a mapping that names the bound and refuses one that
/// only has a wildcard, a comment, or another area's bound (the inverse
/// control the census needs: a detector that answered "mapped" for everything
/// would pass the test above).
#[test]
fn the_detector_distinguishes_a_named_arm_from_its_absence() {
    let named =
        "match error {\n    SitesOpError::Bound(_) => ErrorCode::SystemFailure,\n    _ => x,\n}";
    let wildcard_only = "match error {\n    _ => ErrorCode::SystemFailure,\n}";
    let commented = "match error {\n    // SitesOpError::Bound(_) => later\n    _ => x,\n}";
    let other_area = "match error {\n    PhpOpError::Bound(_) => y,\n    _ => x,\n}";

    let guarded = "match error {\n    SitesOpError::Bound(..) if false => y,\n    _ => x,\n}";

    assert!(names_the_bound_arm(named, "SitesOpError"));
    assert!(!names_the_bound_arm(guarded, "SitesOpError"));
    assert!(!names_the_bound_arm(wildcard_only, "SitesOpError"));
    assert!(!names_the_bound_arm(commented, "SitesOpError"));
    assert!(!names_the_bound_arm(other_area, "SitesOpError"));
}

/// The enum reader finds the declaration of an error that carries a bound and
/// ignores one that does not.
#[test]
fn the_enum_reader_finds_only_errors_that_carry_a_bound() {
    let carrying = "#[derive(Debug)]\npub enum DemoError {\n    Bound(#[from] BoundExceeded),\n}";
    let plain = "#[derive(Debug)]\npub enum PlainError {\n    Other,\n}";

    assert_eq!(
        enum_carrying_a_bound(carrying).as_deref(),
        Some("DemoError")
    );
    assert_eq!(enum_carrying_a_bound(plain), None);
}
