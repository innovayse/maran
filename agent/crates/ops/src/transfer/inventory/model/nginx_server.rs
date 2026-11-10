//! What one nginx `server` block says that an inventory needs.

/// The facts of one `server` block.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct NginxServer {
    /// The words of every `server_name` directive, unvalidated.
    pub server_names: Vec<String>,
    /// The `root` set directly in the block.
    pub root: Option<String>,
    /// The first `root` set inside a nested block, used when the block sets none.
    pub location_root: Option<String>,
    /// The target of the first `fastcgi_pass`.
    pub fastcgi_pass: Option<String>,
    /// Whether a `proxy_pass` appears anywhere in the block.
    pub has_proxy_pass: bool,
}
