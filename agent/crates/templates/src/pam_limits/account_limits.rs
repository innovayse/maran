//! The `pam_limits` drop-in that holds one account's open-files limit.

use askama::Template;

use crate::render_error::RenderError;

/// Renders an account's `limits.d` fragment: one
/// `<account> - nofile <figure>` line under a header comment.
///
/// The `-` type sets the soft and the hard limit to the same figure, for the
/// reason the account's disk quota does: a soft limit below the hard one only
/// buys the account a way to raise it again with `ulimit`, which is the opposite
/// of a ceiling the operator chose.
///
/// `account` is a validated `AccountName` (lowercase letters, digits and `_`)
/// and `open_files` is a validated `OpenFilesLimit` figure, an integer. The
/// file is line-oriented and `pam_limits` reads it as root's decision about a
/// login session, so a newline in either value would be a second rule of
/// somebody else's choosing - `* - nofile 1`, say, or a `maxlogins` line for
/// another user (rules/security.md §4). The values are validated, not escaped,
/// and neither can carry a separator, a `*`, a `@` or a `%` that would turn
/// the line into a rule for a group or for everyone.
#[derive(Template)]
// A config file is not a document: HTML-escaping would corrupt a value
// silently. Values reaching a template are validated (rules/rust.md
// "Validation first"), which is what makes an escaper needless here.
#[template(path = "pam_limits/account_limits.conf.j2", escape = "none")]
pub struct AccountLimits<'a> {
    /// The account the limit applies to; the line's domain and the header's name.
    pub account: &'a str,
    /// The most files one session may hold open, from `OpenFilesLimit::value`.
    pub open_files: u32,
}

impl AccountLimits<'_> {
    /// Renders the file text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
