//! Which directory entries are a site's rotated logs, and which generation.

use maran_agent_core::validation::web::domain::Domain;

/// The most digits a generation number may have. A rotation count above this
/// is not something `logrotate` writes under a policy that keeps fourteen.
const MAXIMUM_DIGITS: usize = 9;

/// The generation of `entry` when it is exactly one of `domain`'s rotated logs.
///
/// Matches the WHOLE entry name against
/// `<domain>.access.log.<N>`, `<domain>.access.log.<N>.gz`, and the same two
/// for `error`, where `<N>` is a decimal number without a leading zero and
/// above zero; anything else is `None`. The live file (`<domain>.access.log`)
/// has no number and is never matched, and neither is generation `0`.
///
/// **Why this cannot reach another site's files.** The entry must START with
/// `<domain>.access.log.` or `<domain>.error.log.` and what follows must be
/// digits and nothing else. Another site's entries start with that other
/// domain, so they match only when the two domains are equal. A look-alike such
/// as the site `a.com.access` (files `a.com.access.access.log...`) or
/// `a.com.access.log` (files `a.com.access.log.access.log...`) fails at the
/// first non-digit. A decision made by parsing text is only safe because the
/// pattern is anchored at both ends and has no wildcard; there is no regular
/// expression here to backtrack into.
#[must_use]
pub(crate) fn rotated_log_name(entry: &str, domain: &Domain) -> Option<u32> {
    let rest = ["access", "error"].iter().find_map(|kind| {
        entry
            .strip_prefix(domain.as_str())?
            .strip_prefix('.')?
            .strip_prefix(*kind)?
            .strip_prefix(".log.")
    })?;

    let digits = rest.strip_suffix(".gz").unwrap_or(rest);

    if digits.is_empty()
        || digits.len() > MAXIMUM_DIGITS
        || digits.starts_with('0')
        || !digits.bytes().all(|byte| byte.is_ascii_digit())
    {
        return None;
    }

    digits.parse().ok()
}

#[cfg(test)]
#[path = "../tests/sites/rotated_log_name_tests.rs"]
mod tests;
