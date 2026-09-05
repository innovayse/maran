//! What serves a site's content.

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_agent_core::validation::web::upstream::Upstream;

/// What serves a site's content, with the data each shape needs.
///
/// An enum rather than a string plus a bag of optional fields: a PHP site
/// without a version and a proxied site without an upstream are states the
/// operations would otherwise have to check for at every use, and one of them
/// would eventually be missed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SiteKind {
    /// Files only.
    Static,
    /// php-fpm, bound to an installed version.
    Php {
        /// The validated two-component version, e.g. `8.3`.
        ///
        /// A [`PhpVersion`] and never a `String`: this value is interpolated
        /// into the socket path and written into `fastcgi_pass unix:…;` inside
        /// a config file root owns, through a template that escapes nothing by
        /// design. A raw string here would let `;`, `}` or a newline add
        /// directives of the caller's choosing to a configuration `nginx -t`
        /// then accepts as valid.
        version: PhpVersion,
    },
    /// Forwarded to a private upstream.
    ReverseProxy {
        /// The validated `host:port`.
        upstream: Upstream,
    },
}
