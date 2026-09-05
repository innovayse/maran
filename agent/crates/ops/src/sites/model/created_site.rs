//! The result of creating a site.

/// What creating a site produced.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CreatedSite {
    /// The absolute document root that was created inside the account's home.
    pub document_root: String,
    /// The absolute path of the vhost the agent wrote and now owns.
    pub config_path: String,
}
