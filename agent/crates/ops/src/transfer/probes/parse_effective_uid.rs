//! Reads the answer of the effective-uid probe.

/// The user id `text` (the output of `id -u`) holds: one decimal number, with
/// surrounding white space ignored. Anything else, including a sign, a second
/// token or an out-of-range number, is `None`.
#[must_use]
pub fn parse_effective_uid(text: &str) -> Option<u32> {
    let token = text.trim();

    if token.is_empty() || !token.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }

    token.parse().ok()
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_effective_uid_tests.rs"]
mod tests;
