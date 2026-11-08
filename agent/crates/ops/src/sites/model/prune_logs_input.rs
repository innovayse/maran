//! What a log prune is asked for.

use maran_agent_core::validation::system::log_retention::LogRetention;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

/// The site whose rotated logs are pruned, and how many rotations stay.
#[derive(Debug, Clone)]
pub struct PruneLogsInput {
    /// The owning account.
    pub account: AccountName,
    /// The site whose rotated logs are pruned.
    pub domain: Domain,
    /// Rotations kept; archives numbered above it are removed.
    pub retain: LogRetention,
}
