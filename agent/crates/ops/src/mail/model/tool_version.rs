//! A version a daemon printed about itself.

/// The longest version token kept; anything longer is not a version number.
const MAXIMUM_TOKEN_LENGTH: usize = 32;

/// The version a daemon reports, with its first two numbers read out.
///
/// The text is kept for display and never compared as a string: only the numbers
/// mean anything to a caller, and only the first two (the Dovecot configuration
/// generation is `major.minor`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ToolVersion {
    /// The version as the daemon spelled it, for example `2.3.19.1`.
    text: String,
    /// The first number.
    major: u32,
    /// The second number.
    minor: u32,
}

impl ToolVersion {
    /// Reads the version out of what a daemon printed for `--version` or the
    /// equivalent: the first whitespace-separated word that starts with a digit,
    /// contains a dot, and is made only of letters, digits and `. - + ~ _`.
    ///
    /// Anything else is `None`. A banner that moves the version to another word,
    /// or a refusal printed in its place, reads as "not observed" and never as a
    /// guessed number.
    #[must_use]
    pub fn parse_output(output: &str) -> Option<Self> {
        output.split_whitespace().find_map(Self::from_token)
    }

    /// The version a single word spells, or `None` when it is not one.
    fn from_token(token: &str) -> Option<Self> {
        if token.len() > MAXIMUM_TOKEN_LENGTH
            || !token.starts_with(|c: char| c.is_ascii_digit())
            || !token.contains('.')
            || !token
                .chars()
                .all(|c| c.is_ascii_alphanumeric() || ".-+~_".contains(c))
        {
            return None;
        }
        let mut numbers = token.split('.').map(leading_number);
        let major = numbers.next()??;
        let minor = numbers.next()??;
        Some(Self {
            text: token.to_owned(),
            major,
            minor,
        })
    }

    /// The version as the daemon spelled it.
    #[must_use]
    pub fn text(&self) -> &str {
        &self.text
    }

    /// The first number.
    #[must_use]
    pub fn major(&self) -> u32 {
        self.major
    }

    /// The second number.
    #[must_use]
    pub fn minor(&self) -> u32 {
        self.minor
    }
}

/// The number a dotted component starts with, or `None` when it starts with no digit.
fn leading_number(component: &str) -> Option<u32> {
    let digits: String = component.chars().take_while(char::is_ascii_digit).collect();
    digits.parse().ok()
}

#[cfg(test)]
#[path = "../../tests/mail/tool_version_tests.rs"]
mod tests;
