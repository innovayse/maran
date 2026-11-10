//! The per-site options an Apache vhost's body is rendered with.

use crate::apache::redirect_rule::RedirectRule;
use crate::apache::site_access_rules::SiteAccessRules;
use crate::apache::site_option_locations::SiteOptionLocations;
use crate::nginx::ip_rules::IpRules;
use crate::nginx::redirect_line::RedirectLine;
use crate::render_error::RenderError;

/// The largest request body a site accepts when its options do not say, in mebibytes.
///
/// The same figure the nginx body writes, so a site answers an upload the same way whichever
/// web server the host chose; the figure is the largest value `upload_max_filesize` /
/// `post_max_size` accept (`MAXIMUM_BYTES` in `ops/src/php/model/php_override.rs`).
const DEFAULT_BODY_LIMIT_MIB: u64 = 512;

/// Bytes in a mebibyte.
const BYTES_PER_MIB: u64 = 1_048_576;

/// A site's per-site options, as the Apache templates read them.
///
/// There is no request-rate field: Apache's stock modules have no per-address request limit,
/// and a site that asks for one is refused where it is created (see
/// `ops::sites::model::site_options::SiteOptions::check_web_server`), so the template never
/// has to render a half of a promise.
#[derive(Debug, Clone, Copy)]
pub struct SiteOptionsBlock<'a> {
    /// The request body ceiling in mebibytes, when the site sets one.
    pub client_max_body_size_mib: Option<u32>,
    /// Days static assets may be cached, when the site sets the rule.
    pub static_cache_days: Option<u32>,
    /// The site's redirect rules.
    pub redirects: &'a [RedirectLine<'a>],
    /// Absolute path of the credential file, when the site has basic-auth users.
    pub auth_user_file: Option<&'a str>,
    /// The site's address list, when it has one.
    pub ip_rules: Option<IpRules<'a>>,
}

impl SiteOptionsBlock<'static> {
    /// A site with no options: the rendering every site had before options existed.
    pub const NONE: SiteOptionsBlock<'static> = SiteOptionsBlock {
        client_max_body_size_mib: None,
        static_cache_days: None,
        redirects: &[],
        auth_user_file: None,
        ip_rules: None,
    };
}

impl SiteOptionsBlock<'_> {
    /// Whether the site restricts who may read it.
    #[must_use]
    pub fn restricts_access(&self) -> bool {
        self.auth_user_file.is_some() || self.ip_rules.is_some()
    }

    /// The request body ceiling in bytes: the site's own, or the default.
    #[must_use]
    pub fn body_limit_bytes(&self) -> u64 {
        let mebibytes = self
            .client_max_body_size_mib
            .map_or(DEFAULT_BODY_LIMIT_MIB, u64::from);
        mebibytes * BYTES_PER_MIB
    }

    /// The redirect rules in the form `RedirectMatch` takes.
    #[must_use]
    pub fn redirect_rules(&self) -> Vec<RedirectRule> {
        self.redirects.iter().map(RedirectRule::from_line).collect()
    }

    /// Whether the body has anything to render after the site's own locations.
    ///
    /// `serves_files` is false for the proxy arm, whose responses come from the upstream and
    /// which therefore has no static cache rule.
    #[must_use]
    pub fn has_locations(&self, serves_files: bool) -> bool {
        !self.redirects.is_empty() || (serves_files && self.static_cache_days.is_some())
    }

    /// Renders the authorisation lines.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template fails.
    pub fn render_access_rules(&self) -> Result<String, RenderError> {
        SiteAccessRules {
            auth_user_file: self.auth_user_file,
            ip_rules: self.ip_rules,
        }
        .render_config()
    }

    /// Renders the redirect rules and the cache rule.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template fails.
    pub fn render_locations(&self, serves_files: bool) -> Result<String, RenderError> {
        SiteOptionLocations {
            redirects: self.redirect_rules(),
            cache_days: if serves_files {
                self.static_cache_days
            } else {
                None
            },
        }
        .render_config()
    }
}

#[cfg(test)]
#[path = "../tests/apache/site_options_block_tests.rs"]
mod tests;
