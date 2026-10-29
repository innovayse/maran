//! Tests for the `AdapterBinary` type.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `adapter_binary.rs` declares this file with `#[path]`, which keeps it a child
//! module and therefore able to reach the crate-private constructors.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::path::Path;

use super::AdapterBinary;

/// The source of the type, read as text for the architecture guards below.
const SOURCE: &str = include_str!("../adapter_binary.rs");

/// The lines of `source` that offer a way to build an `AdapterBinary` from
/// outside this crate, or a way around the private field.
///
/// Comment lines are skipped, because the type's own documentation names the
/// forbidden routes in order to forbid them.
fn escape_hatches(source: &str) -> Vec<String> {
    source
        .lines()
        .map(str::trim)
        .filter(|line| !line.starts_with("//"))
        .filter(|line| {
            let returns_self = line.contains("-> Self") || line.contains("-> AdapterBinary");
            let is_public = line.starts_with("pub fn") || line.starts_with("pub const fn");
            let public_constructor = is_public && returns_self;
            public_constructor
                || line.contains("impl From<")
                || line.contains("FromStr")
                || line.contains("Default")
                || line.contains("Deserialize")
                || line.contains("cfg(feature")
        })
        .map(str::to_owned)
        .collect()
}

/// A path displays as itself, debugs with the path inside, and compares to a `str`.
#[test]
fn an_adapter_binary_displays_and_debugs_as_its_path_and_compares_to_a_str() {
    let binary = AdapterBinary::fixed("/usr/bin/id");

    assert_eq!(binary.to_string(), "/usr/bin/id");
    assert_eq!(format!("{binary:?}"), "AdapterBinary(\"/usr/bin/id\")");
    assert_eq!(binary, "/usr/bin/id");
    assert!(binary == *"/usr/bin/id");
    assert!(binary != *"/usr/bin/other");
    assert!(binary != "/usr/bin/other");
}

/// Every view of the path (`str`, `Path`, `OsStr`) is the same text.
#[test]
fn an_adapter_binary_exposes_the_same_path_as_str_path_and_os_str() {
    let binary = AdapterBinary::fixed("/usr/sbin/nft");

    assert_eq!(binary.as_str(), "/usr/sbin/nft");
    assert_eq!(binary.as_path(), Path::new("/usr/sbin/nft"));
    let as_os: &OsStr = binary.as_ref();
    assert_eq!(as_os, OsStr::new("/usr/sbin/nft"));
    let as_path: &Path = binary.as_ref();
    assert_eq!(as_path, Path::new("/usr/sbin/nft"));
    let as_str: &str = binary.as_ref();
    assert_eq!(as_str, "/usr/sbin/nft");
}

/// A fixed and a versioned binary with the same text are equal; different text is not.
#[test]
fn two_adapter_binaries_are_equal_when_their_paths_are_equal() {
    let fixed = AdapterBinary::fixed("/usr/bin/id");
    let built = AdapterBinary::versioned(String::from("/usr/bin/id"));
    let other = AdapterBinary::fixed("/usr/bin/quota");

    assert_eq!(fixed, built);
    assert_ne!(fixed, other);
}

/// The type offers no route from a string, a default, a feature or a public
/// constructor to a value.
///
/// A source scan complements the compile-fail doctests: it also sees a
/// `cfg(feature = ...)` escape hatch, which a doctest (built without features)
/// would not.
#[test]
fn the_adapter_binary_source_offers_no_constructor_outside_the_crate() {
    let found = escape_hatches(SOURCE);

    assert!(
        found.is_empty(),
        "escape hatches in adapter_binary.rs: {found:?}"
    );
}

/// The scan really recognises each forbidden route, and sees the legitimate
/// crate-private constructor as allowed.
#[test]
fn the_escape_hatch_scan_flags_every_forbidden_route_and_passes_the_real_constructors() {
    for forbidden in [
        "impl From<&'static str> for AdapterBinary {",
        "impl std::str::FromStr for AdapterBinary {",
        "#[derive(Default)]",
        "#[derive(Deserialize)]",
        "#[cfg(feature = \"test-support\")]",
        "pub fn new(path: String) -> Self {",
        "pub const fn fixed(path: &'static str) -> Self {",
    ] {
        assert_eq!(
            escape_hatches(forbidden).len(),
            1,
            "{forbidden} must be flagged"
        );
    }

    assert!(escape_hatches("pub(crate) const fn fixed(path: &'static str) -> Self {").is_empty());
    assert!(escape_hatches("pub fn as_str(&self) -> &str {").is_empty());
    assert!(escape_hatches("/// ```compile_fail impl From<").is_empty());
    assert!(SOURCE.contains("pub(crate) const fn fixed"));
}
