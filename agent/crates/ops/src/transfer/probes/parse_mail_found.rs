//! Reads the answer of the mail-store probe.

/// Whether `text` is the word `yes`, white space aside. Anything else, `no`
/// included, is `false`.
#[must_use]
pub fn parse_mail_found(text: &str) -> bool {
    text.trim() == "yes"
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_mail_found_tests.rs"]
mod tests;
