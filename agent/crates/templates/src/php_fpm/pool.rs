//! The php-fpm pool for one account × PHP version.

use askama::Template;

use crate::php_fpm::pool_override::PoolOverride;
use crate::render_error::RenderError;

/// Renders one php-fpm pool file: one account, one PHP version, one worker
/// budget from the account's plan (spec §8, §11).
///
/// Every field is a value the caller has already validated. The two
/// hardening lines — `open_basedir` and `disable_functions` — are set with
/// `php_admin_value`, deliberately, so nothing in [`Self::overrides`] can
/// shadow them: `php_admin_value` wins over `php_value` regardless of order.
#[derive(Template)]
// A config file is not a document: `escape = "none"` because HTML-escaping
// nginx directives corrupts them silently — an apostrophe in a comment came
// out as `&#x27;`, and a body embedded in two blocks was escaped twice. Values
// reaching a template are VALIDATED, never escaped (rules/security.md §4,
// rules/rust.md "Validation first"): `Domain`, `Upstream` and
// `resolve_in_home` are what make them safe to write, and an escaper here
// would only hide a value that had not been through them.
#[template(path = "php-fpm/pool.conf.j2", escape = "none")]
pub struct Pool<'a> {
    /// The pool's section name, `[name]` in the rendered file.
    pub pool_name: &'a str,
    /// The system account the pool's workers run as.
    pub account: &'a str,
    /// Absolute path of the pool's unix socket.
    pub socket_path: &'a str,
    /// The user and group the web server runs as, given ownership of the
    /// socket so it can connect to it.
    pub web_server_user: &'a str,
    /// Maximum worker processes, materialised from the account's plan.
    pub max_children: u32,
    /// Worker processes started at boot.
    pub start_servers: u32,
    /// Minimum idle worker processes.
    pub min_spare_servers: u32,
    /// Maximum idle worker processes.
    pub max_spare_servers: u32,
    /// The account's home directory, the root of what `open_basedir` grants.
    pub home_directory: &'a str,
    /// Where PHP writes this account's session files.
    ///
    /// Named explicitly and set admin-side because the alternative is PHP's
    /// default, which falls back to the shared, world-readable `/tmp` when the
    /// packaged session directory is not writable by the account — and that
    /// fallback is one customer's session tokens sitting where every other
    /// customer's PHP can read them.
    pub session_directory: &'a str,
    /// Where PHP writes this account's in-flight uploads, for the same reason.
    pub upload_temporary_directory: &'a str,
    /// Seconds after which php-fpm kills a request outright.
    ///
    /// Not the same limit as `max_execution_time`, which does not count time
    /// spent in a blocking syscall: a request waiting on a dead database is
    /// never counted by PHP and is reclaimed only by this.
    pub request_terminate_timeout: u32,
    /// The whitelisted php.ini overrides the customer has set (spec §11).
    pub overrides: &'a [PoolOverride<'a>],
}

impl Pool<'_> {
    /// Renders the configuration text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
