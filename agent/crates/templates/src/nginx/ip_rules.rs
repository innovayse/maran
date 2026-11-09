//! A site's IP allow or deny list, as the template writes it.

/// The networks a site lets in, or keeps out.
///
/// Each network is the canonical spelling a `SourceCidr` renders (an `IpAddr`
/// and a prefix length), so nothing but digits, hex digits, `.`, `:` and `/`
/// can reach the template.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct IpRules<'a> {
    /// True when only the listed networks may enter (the list ends in
    /// `deny all;`), false when the listed networks are refused (the list ends
    /// in `allow all;`).
    pub allow_list: bool,
    /// The listed networks, in the order they are written.
    pub networks: &'a [String],
}
