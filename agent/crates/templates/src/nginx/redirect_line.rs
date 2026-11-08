//! One redirect rule, as the template writes it.

/// A redirect rule reduced to the four facts the template needs.
///
/// Every text field is a value the caller has already validated
/// (`RedirectPath`, `RedirectTarget` in `agent-core`): a template escapes
/// nothing, and a character outside those types' alphabets never reaches this
/// struct (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RedirectLine<'a> {
    /// The request path the rule applies to. A prefix rule's path ends in `/`.
    pub path: &'a str,
    /// True for a prefix rule (the path and everything under it), false for an
    /// exact one.
    pub prefix: bool,
    /// Where the rule sends the visitor. A prefix rule's target ends in `/`,
    /// because the rest of the request path is appended to it.
    pub target: &'a str,
    /// True for a 301, false for a 302.
    pub permanent: bool,
}

impl RedirectLine<'_> {
    /// The regular expression a prefix rule's `rewrite` matches: the path with
    /// its dots escaped, then everything below it captured as `$1`.
    ///
    /// The path's alphabet is letters, digits, `.`, `_`, `~`, `/` and `-`,
    /// and only `.` means something else in a regular expression, so it is the
    /// only character escaped. The caller guarantees the alphabet; this does
    /// not re-check it.
    #[must_use]
    pub fn rewrite_pattern(&self) -> String {
        format!("^{}(.*)$", self.path.replace('.', "\\."))
    }
}

#[cfg(test)]
#[path = "../tests/nginx/redirect_line_tests.rs"]
mod tests;
