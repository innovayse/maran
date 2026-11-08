//! The server-level lines that limit how fast one address may request a site.

use askama::Template;

use crate::nginx::request_rate_line::RequestRateLine;
use crate::render_error::RenderError;

/// Renders `limit_req` and the two lines that say how a refusal looks.
///
/// The zone it names is declared at the top of the vhost file by the site's own
/// template, because `limit_req_zone` is valid only at `http` level, where the
/// file is included, and `limit_req` only at `server` or `location` level.
///
/// A request over the limit is answered `429`, not nginx's default `503`: a
/// refused visitor is not a failing server, and a monitor that watches for
/// `503` should not fire for it. It is logged at `warn`, below the `error` that
/// nginx's default would write into the customer's own error log for every
/// refused request.
///
/// `nodelay` is written only with a burst. A burst without it QUEUES the extra
/// requests and releases them at the sustained rate, which turns a spike into
/// slow pages; with it they are served at once and the budget is spent. With
/// no burst there is no queue to skip, so the keyword would say nothing.
///
/// Every value is validated by the caller (see [`RequestRateLine`]).
#[derive(Template)]
// A config file is not a document; values are validated, never escaped
// (rules/security.md §4, rules/rust.md "Validation first").
#[template(path = "nginx/site_request_limit.conf.j2", escape = "none")]
pub struct SiteRequestLimit<'a> {
    /// The limit to write.
    pub rate: RequestRateLine<'a>,
}

impl SiteRequestLimit<'_> {
    /// Renders the lines without a trailing newline.
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
