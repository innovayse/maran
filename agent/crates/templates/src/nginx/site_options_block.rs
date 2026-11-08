//! The per-site options a vhost's body is rendered with (issue #151).

use crate::nginx::ip_rules::IpRules;
use crate::nginx::redirect_line::RedirectLine;
use crate::nginx::request_rate_line::RequestRateLine;
use crate::nginx::site_access_rules::SiteAccessRules;
use crate::nginx::site_option_locations::SiteOptionLocations;
use crate::nginx::site_request_limit::SiteRequestLimit;
use crate::render_error::RenderError;

/// What a site's options add to the vhost it is rendered into.
///
/// The options are data here, not a template: they land in two different places
/// of the body, and each place has a small template of its own. The access
/// rules (`auth_basic`, `allow` / `deny`) go at server level before the first
/// `location`, so every location inherits them; the extra locations (redirects,
/// the static-asset cache rule) go after the locations the body already has —
/// the cache rule is a regular-expression location, nginx tries those in order,
/// and it must come after the one that denies dot-files or a request for
/// `/.env.css` would be served.
///
/// [`Self::NONE`] adds nothing, and a body rendered with it is byte-identical
/// to one rendered before the options existed: every golden of a site without
/// options pins that.
///
/// A redirect is answered in nginx's rewrite phase, before the access phase, so
/// it is answered even for a client the IP list or the password would refuse.
/// A redirect reveals only the target its owner wrote.
///
/// Every text field is a value the caller has validated; a template escapes
/// nothing (rules/security.md item 4).
#[derive(Debug, Clone, Copy)]
pub struct SiteOptionsBlock<'a> {
    /// `client_max_body_size` in MiB; `None` leaves the body's own default.
    pub client_max_body_size_mib: Option<u32>,
    /// Days a browser may cache static assets; `None` writes no cache rule.
    /// Only a body that serves files renders it.
    pub static_cache_days: Option<u32>,
    /// The redirect rules, in the order they are written.
    pub redirects: &'a [RedirectLine<'a>],
    /// The absolute path of the credential file, when the site has HTTP basic
    /// authentication. The agent derives it from the domain
    /// (`AgentPaths::site_auth_file`); no caller supplies it.
    pub auth_user_file: Option<&'a str>,
    /// The IP rules, when the site has any.
    pub ip_rules: Option<IpRules<'a>>,
    /// The per-address request limit, when the site has one (issue #159).
    ///
    /// Two places read it: the site's own template declares the zone at the top
    /// of the file, and the body writes `limit_req` at server level. Both read
    /// this one value, so the zone a `limit_req` names is the zone declared.
    pub request_rate: Option<RequestRateLine<'a>>,
}

impl SiteOptionsBlock<'static> {
    /// The options of a site that has none.
    pub const NONE: SiteOptionsBlock<'static> = SiteOptionsBlock {
        client_max_body_size_mib: None,
        static_cache_days: None,
        redirects: &[],
        auth_user_file: None,
        ip_rules: None,
        request_rate: None,
    };
}

impl SiteOptionsBlock<'_> {
    /// Whether the site restricts who may read it, by password or by address.
    ///
    /// The vhost's certificate-challenge location asks this: when it is true the
    /// location must say `auth_basic off;` and `allow all;`, or it would inherit
    /// the restriction and the certificate authority would be refused.
    #[must_use]
    pub fn restricts_access(&self) -> bool {
        self.auth_user_file.is_some() || self.ip_rules.is_some()
    }

    /// Whether the site has a request limit, so the body writes `limit_req`.
    #[must_use]
    pub fn limits_requests(&self) -> bool {
        self.request_rate.is_some()
    }

    /// Renders the server-level request-limit lines, without a trailing newline.
    ///
    /// Empty when the site has no limit; a caller asks [`Self::limits_requests`]
    /// first so it writes no blank line for nothing.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and its type have drifted apart.
    pub fn render_request_limit(&self) -> Result<String, RenderError> {
        match self.request_rate {
            Some(rate) => SiteRequestLimit { rate }.render_config(),
            None => Ok(String::new()),
        }
    }

    /// Whether [`Self::render_locations`] writes anything for a body that does
    /// (`serves_files`) or does not serve files.
    #[must_use]
    pub fn has_locations(&self, serves_files: bool) -> bool {
        !self.redirects.is_empty() || (serves_files && self.static_cache_days.is_some())
    }

    /// Renders the server-level access rules, without a trailing newline.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and its type have drifted apart.
    pub fn render_access_rules(&self) -> Result<String, RenderError> {
        SiteAccessRules {
            auth_user_file: self.auth_user_file,
            ip_rules: self.ip_rules,
        }
        .render_config()
    }

    /// Renders the extra locations — redirects, then the cache rule — without
    /// a trailing newline.
    ///
    /// `serves_files` is false for a reverse-proxied body, which has no files
    /// to cache; the cache rule is then left out. `front_controller` is true
    /// for a PHP body, whose cache rule falls back to `index.php` for a file
    /// that does not exist, as the body's own `location /` does.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails.
    pub fn render_locations(
        &self,
        serves_files: bool,
        front_controller: bool,
    ) -> Result<String, RenderError> {
        SiteOptionLocations {
            redirects: self.redirects,
            cache_days: if serves_files {
                self.static_cache_days
            } else {
                None
            },
            front_controller,
        }
        .render_config()
    }
}

#[cfg(test)]
#[path = "../tests/nginx/site_options_block_tests.rs"]
mod tests;
