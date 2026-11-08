//! The extra `location` blocks a site's options add.

use askama::Template;

use crate::nginx::redirect_line::RedirectLine;
use crate::render_error::RenderError;

/// Renders the redirect locations and the static-asset cache location.
///
/// An exact rule is `location = <path> { return <code> <target>; }`. A prefix
/// rule is `location ^~ <path> { rewrite ^<path>(.*)$ <target>$1 <flag>; }`:
/// `^~` makes it win over the regular-expression locations of the body (a
/// redirect of `/` must not be pre-empted by `\.php$`), and `rewrite` appends
/// the rest of the path and the query string to the target. Neither can reach
/// the certificate challenge: the path type refuses that directory.
///
/// The cache rule is `location ~* \.(<fixed extensions>)$`. The extension list
/// is a literal in the template — no option names an extension — and the rule
/// serves the file with `try_files`, falling back to `index.php` for a PHP site
/// and to a 404 for a static one. The server-level `root` and
/// `disable_symlinks` are inherited by it.
///
/// Every value is validated by the caller (rules/security.md item 4).
#[derive(Template)]
// A config file is not a document; values are validated, never escaped
// (rules/security.md §4, rules/rust.md "Validation first").
#[template(path = "nginx/site_option_locations.conf.j2", escape = "none")]
pub struct SiteOptionLocations<'a> {
    /// The redirect rules, in the order they are written.
    pub redirects: &'a [RedirectLine<'a>],
    /// Days a browser may cache static assets; `None` writes no cache rule.
    pub cache_days: Option<u32>,
    /// True when the cache rule falls back to `index.php` for a missing file.
    pub front_controller: bool,
}

impl SiteOptionLocations<'_> {
    /// Renders the locations without a trailing newline.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render()
            .map(|text| text.trim_end().to_owned())
            .map_err(RenderError::Askama)
    }
}
