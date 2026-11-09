//! A site's credential file for HTTP basic authentication.

use askama::Template;

use crate::nginx::auth_line::AuthLine;
use crate::render_error::RenderError;

/// Renders the file `auth_basic_user_file` names: one `name:hash` line per
/// user, each ending in a newline.
///
/// nginx reads this file on every request, so it needs no reload. The values
/// are validated, never escaped (rules/security.md item 4).
#[derive(Template)]
// A credential file is not a document: nothing is escaped, everything is validated.
#[template(path = "nginx/site_auth_file.htpasswd.j2", escape = "none")]
pub struct SiteAuthFile<'a> {
    /// The users, in the order they are written.
    pub users: &'a [AuthLine<'a>],
}

impl SiteAuthFile<'_> {
    /// Renders the file.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}

#[cfg(test)]
#[path = "../tests/nginx/site_auth_file_tests.rs"]
mod tests;
