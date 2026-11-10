//! Which web server owns ports 80 and 443 for the hosted sites.

/// The web server a host serves its sites with (issue #517).
///
/// Exactly one is installed at a time. A value of this type is the only thing the site
/// operations branch on when they pick a binary, a service, an identity or a template,
/// so no operation ever writes "nginx" or "apache" as a literal of its own: the answers
/// come from [`WebServerFacts`](crate::web_server_facts::WebServerFacts), asked with this
/// value.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum SiteWebServer {
    /// nginx, which is also the panel's own front on its own port.
    Nginx,
    /// Apache httpd.
    Apache,
}
