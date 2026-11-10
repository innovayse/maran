//! What serves a site found on a source.

/// What serves a site. The variants are ordered by precedence (`Static` <
/// `Proxy` < `Php`): a virtual host that shows several signs takes the strongest,
/// because a PHP handler beside a proxy rule is a PHP site.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash, Default)]
pub enum SourceSiteBackend {
    /// Static files, and the default when nothing else is seen.
    #[default]
    Static,
    /// A reverse proxy to an upstream.
    Proxy,
    /// PHP, through FastCGI or a handler.
    Php,
}
