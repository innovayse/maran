//! Whether a package manager's reason for a failed fetch is an HTTP answer.

/// Whether `text` opens with a three-digit HTTP status code followed by a
/// space — the way both families' package managers quote a source that
/// answered (`404  Not Found` from apt, `404 for http://…` after dnf's
/// `Status code: `).
///
/// One question asked by both [`crate::debian::debian_packages`] and
/// [`crate::rhel::rhel_packages`], so it is answered once, here, and not once
/// per family: what separates "answered and refused" from "never reached" must
/// not drift between the two.
pub(crate) fn starts_with_http_status(text: &str) -> bool {
    let mut bytes = text.bytes();
    let digits = bytes.by_ref().take(3).filter(u8::is_ascii_digit).count();
    digits == 3 && bytes.next() == Some(b' ')
}

#[cfg(test)]
#[path = "tests/starts_with_http_status_tests.rs"]
mod tests;
