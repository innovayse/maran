//! One redirect rule, as the Apache template writes it.

use crate::nginx::redirect_line::RedirectLine;

/// A site's redirect rule in the form `RedirectMatch` takes: an anchored pattern, a status
/// number and a replacement.
///
/// Built from the web-server-neutral [`RedirectLine`] the options already carry, so the two
/// web servers render one list of rules; only the spelling differs.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RedirectRule {
    /// The anchored regular expression the request path must match.
    pub pattern: String,
    /// `301` for a permanent rule, `302` for a temporary one.
    pub status: u16,
    /// Where the visitor is sent; a prefix rule ends in `$1`, which carries the rest of the path.
    pub replacement: String,
}

impl RedirectRule {
    /// Builds the rule for `line`.
    ///
    /// The path is a validated `RedirectPath`, whose alphabet is letters, digits and
    /// `. _ ~ / -`, so the dot is the only character that is special in a pattern and is the
    /// only one escaped.
    #[must_use]
    pub fn from_line(line: &RedirectLine<'_>) -> Self {
        let escaped = line.path.replace('.', r"\.");
        let (pattern, replacement) = if line.prefix {
            (format!("^{escaped}(.*)$"), format!("{}$1", line.target))
        } else {
            (format!("^{escaped}$"), line.target.to_owned())
        };

        Self {
            pattern,
            status: if line.permanent { 301 } else { 302 },
            replacement,
        }
    }
}

#[cfg(test)]
#[path = "../tests/apache/redirect_rule_tests.rs"]
mod tests;
