//! Reads the answer of the os-release probe.

/// The most characters of a release name that are kept.
const LONGEST_RELEASE: usize = 128;

/// The `PRETTY_NAME` in `text`, with one pair of surrounding quotes removed.
///
/// Only the first line is read and it must begin with `PRETTY_NAME=`. The value
/// is `None` when it is empty, longer than 128 characters, or holds a control
/// character: it is shown to an administrator and must not carry an escape
/// sequence or a line break from a hostile source.
#[must_use]
pub fn parse_os_release(text: &str) -> Option<String> {
    let value = text.lines().next()?.strip_prefix("PRETTY_NAME=")?.trim();
    let unquoted = ['"', '\'']
        .iter()
        .find_map(|quote| value.strip_prefix(*quote)?.strip_suffix(*quote))
        .unwrap_or(value);

    if unquoted.is_empty()
        || unquoted.chars().count() > LONGEST_RELEASE
        || unquoted.chars().any(char::is_control)
    {
        return None;
    }

    Some(unquoted.to_owned())
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_os_release_tests.rs"]
mod tests;
