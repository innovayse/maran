//! Whether a field of a package manager's output is safe to carry on as text.

/// Whether `text` is a non-empty run of printable, non-space ASCII: what a
/// package name, a version or a repository id is, and nothing a log line or a
/// wire field could be confused by.
///
/// One question asked by both [`crate::debian::debian_packages`] and
/// [`crate::rhel::rhel_packages`] of every field they read out of the pending
/// updates, so it is answered once, here: the two readers must not drift on what
/// they accept from text a repository published.
pub(crate) fn is_printable_token(text: &str) -> bool {
    !text.is_empty() && text.bytes().all(|byte| byte.is_ascii_graphic())
}

#[cfg(test)]
#[path = "tests/is_printable_token_tests.rs"]
mod tests;
