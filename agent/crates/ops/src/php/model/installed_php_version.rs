//! One PHP version found installed on this host.

/// A supported PHP version that is present on this host, with the locations
/// the panel needs to talk about it.
///
/// Returned by `list_php_versions`, which the panel calls on every page that
/// offers a version picker — so it carries what that picker needs and nothing
/// that would require running a package manager to learn.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstalledPhpVersion {
    /// The two-component version, as it is written — `8.3`.
    pub version: String,
    /// The directory this version's pool files are written into, from the
    /// adapter: `/etc/php/8.3/fpm/pool.d` on the Debian family,
    /// `/etc/opt/remi/php83/php-fpm.d` on the RHEL family.
    pub pool_directory: String,
    /// The directory this version's pool sockets are created in.
    ///
    /// The agent's own, identical on every family, and the same constant the
    /// site vhost's `fastcgi_pass` is rendered from — reported here so a
    /// reader of the panel can see the two ends named by one value rather
    /// than trust that they agree.
    pub socket_directory: String,
}
