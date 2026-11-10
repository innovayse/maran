//! Reads the answer of the Maran-panel probe.

/// The most characters of a version that are kept.
const LONGEST_VERSION: usize = 64;

/// The version in `text`, whose first line must be `VERSION=<version>` as the
/// panel's release state file spells it, with one pair of quotes removed.
///
/// The version must be a non-empty run of letters, digits and `.`, `_`, `+`, `-`
/// no longer than 64 characters; anything else is `None`. A source that is not a
/// Maran panel prints nothing, and a source that lies is no more trusted than
/// any other text it sends: the value only selects an adapter and is displayed.
#[must_use]
pub fn parse_panel_version(text: &str) -> Option<String> {
    let value = text.lines().next()?.strip_prefix("VERSION=")?.trim();
    let unquoted = ['"', '\'']
        .iter()
        .find_map(|quote| value.strip_prefix(*quote)?.strip_suffix(*quote))
        .unwrap_or(value);

    let allowed = |character: char| {
        character.is_ascii_alphanumeric() || matches!(character, '.' | '_' | '+' | '-')
    };

    if unquoted.is_empty() || unquoted.len() > LONGEST_VERSION || !unquoted.chars().all(allowed) {
        return None;
    }

    Some(unquoted.to_owned())
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_panel_version_tests.rs"]
mod tests;
