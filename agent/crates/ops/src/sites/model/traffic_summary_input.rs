//! What a traffic summary is asked for.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::traffic_cursor::TrafficCursor;

/// The site to summarise and where the previous call ended.
///
/// Validated types only: the log's location is derived from the account and the
/// domain, and no request can name a path.
#[derive(Debug, Clone)]
pub struct TrafficSummaryInput {
    /// The owning account.
    pub account: AccountName,
    /// The site whose access log is read.
    pub domain: Domain,
    /// Where the previous call ended; `None` starts at the previous rotation.
    pub cursor: Option<TrafficCursor>,
}
