//! Whether an IP list names the clients that may enter or those that may not.

/// What the networks of an [`super::ip_access::IpAccess`] mean.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IpAccessMode {
    /// Only the listed networks may reach the site.
    AllowList,
    /// The listed networks are refused; everyone else may reach the site.
    DenyList,
}
