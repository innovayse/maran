//! The one question [`super::domain::Domain::parse`] asks after a name is
//! well-formed: does it name a host no tenant may serve?
//!
//! Subject-named, like `ipv4_disguise.rs`: the file is named after the question,
//! holds one crate-visible predicate and no error type of its own, and the
//! caller reports the refusal through [`DomainError`]. The same static set is
//! spelled by the backend's reserved host name rule; the two are spelled
//! twice and kept equal by review only: nothing compares them mechanically, and each
//! side's tests hold their own literals, so a row added to one test table must be
//! mirrored in the other (`reserved_host_name_tests.rs`, `ReservedHostNameRuleTests.cs`).

use crate::validation::web::domain_error::DomainError;

/// Names under which a name is a special-use one that never names a public
/// host. A name is refused when it IS one of these or ends in `.` plus one.
/// `.test` and `.example` are deliberately absent: fixtures and staging use them.
const SPECIAL_USE_SUFFIXES: [&str; 6] = [
    "local",
    "localdomain",
    "internal",
    "lan",
    "home.arpa",
    "invalid",
];

/// Why `name` is reserved, or `None` when a tenant may use it.
///
/// `name` MUST already be a syntactically valid, lower-case domain (the shape
/// check in [`super::domain::Domain::parse`] has passed): the predicate does no
/// trimming and no case folding, so it never disagrees with the value that is
/// then written into the configuration.
///
/// The three refusals, checked in this order:
///
/// - the FINAL label is all digits: every dotted IPv4 literal, and also `1.2.3`,
///   `10.1` and `12345`, which some resolvers read as address
///   forms. Categorical on purpose, so no list of the server's
///   addresses is needed and none can go stale;
/// - `localhost`, or any name ending `.localhost`;
/// - a name that is, or ends with `.` plus, one of [`SPECIAL_USE_SUFFIXES`].
pub(crate) fn reservation_of(name: &str) -> Option<DomainError> {
    let final_label = name.rsplit('.').next().unwrap_or(name);

    if !final_label.is_empty() && final_label.bytes().all(|b| b.is_ascii_digit()) {
        return Some(DomainError::NumericFinalLabel);
    }

    if name == "localhost" || name.ends_with(".localhost") {
        return Some(DomainError::LocalhostFamily);
    }

    let special_use = SPECIAL_USE_SUFFIXES.iter().any(|suffix| {
        name == *suffix
            || name
                .strip_suffix(suffix)
                .is_some_and(|rest| rest.ends_with('.'))
    });
    if special_use {
        return Some(DomainError::SpecialUseSuffix);
    }

    None
}

#[cfg(test)]
#[path = "../../tests/validation/web/reserved_host_name_tests.rs"]
mod tests;
