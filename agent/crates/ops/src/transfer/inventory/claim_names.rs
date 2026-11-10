//! Validating the host names of one virtual host.

use maran_agent_core::validation::web::domain::Domain;

use super::model::claimed_names::ClaimedNames;

/// Runs every name through [`Domain::parse`], the validator a hand-typed site
/// name passes, and sorts them into a primary, aliases and a count of refusals.
///
/// Skipped without a count, because they are conventions and not names: the
/// empty name and `_` (a catch-all). A leading `.` (nginx's "this name and its
/// subdomains") is dropped first. Everything the validator refuses — a wildcard
/// such as `*.example.com`, a regular expression, an address, a reserved name,
/// a bad label — is counted in `refused` and never repaired. The first name that
/// validates is the primary; a repeat of it or of an earlier alias is dropped.
#[must_use]
pub fn claim_names(raw: &[String]) -> ClaimedNames {
    let mut primary: Option<Domain> = None;
    let mut aliases: Vec<Domain> = Vec::new();
    let mut refused = 0;
    for name in raw {
        let name = name.as_str();
        if name.is_empty() || name == "_" {
            continue;
        }
        let candidate = name.strip_prefix('.').unwrap_or(name);
        match Domain::parse(candidate) {
            Err(_) => refused += 1,
            Ok(domain) => {
                if primary.is_none() {
                    primary = Some(domain);
                } else if primary.as_ref() != Some(&domain) && !aliases.contains(&domain) {
                    aliases.push(domain);
                }
            }
        }
    }
    ClaimedNames {
        primary,
        aliases,
        refused,
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/claim_names_tests.rs"]
mod tests;
