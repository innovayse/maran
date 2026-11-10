//! The host names of one virtual host that a tenant may claim.

use maran_agent_core::validation::web::domain::Domain;

/// The outcome of validating every name a virtual host answers to.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ClaimedNames {
    /// The first name that validated.
    pub primary: Option<Domain>,
    /// The later names that validated, without duplicates.
    pub aliases: Vec<Domain>,
    /// How many names were refused (wildcards, patterns, addresses, reserved
    /// names, anything the domain validator rejects).
    pub refused: usize,
}
