//! The one test every word read out of the daemon's output passes first.

/// Whether `text` is a non-empty run of printable, non-space ASCII.
///
/// Asked of every token taken from `rndc` output before it is compared with
/// anything or turned into a number, so a byte the daemon printed that is not an
/// ordinary character (a control character, a space, an escape sequence, a
/// multi-byte character) is an unreadable answer and never a value. Output is
/// parsed and never echoed: nothing here puts a byte of it into a message.
#[must_use]
pub(crate) fn is_printable_token(text: &str) -> bool {
    !text.is_empty() && text.bytes().all(|byte| byte.is_ascii_graphic())
}

#[cfg(test)]
#[path = "../tests/dns/is_printable_token_tests.rs"]
mod tests;
