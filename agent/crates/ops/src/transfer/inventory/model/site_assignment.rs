//! Which account each virtual host belongs to.

use maran_agent_core::validation::web::domain::Domain;

use super::source_site_record::SourceSiteRecord;

/// The result of attributing virtual hosts to accounts.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SiteAssignment {
    /// The sites of each account, in the order of the accounts passed in.
    pub per_account: Vec<Vec<SourceSiteRecord>>,
    /// The primary names of virtual hosts that belong to no account: a document
    /// root outside every home, or none at all.
    pub unowned: Vec<Domain>,
}
