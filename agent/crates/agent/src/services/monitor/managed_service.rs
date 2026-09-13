//! Which well-known service a status row is about.

use crate::proto::ManagedService;

/// The wire's name for the managed unit at `position` of the agent's report.
///
/// `DistroAdapter::managed_units` fixes both the set and the order — web
/// server, database, cron, OpenSSH — and `get_service_statuses` reports one row
/// per unit in exactly that order, so position IS the identity of a row. That
/// contract is why this maps a position rather than matching the unit's name:
/// matching the name would mean asking the adapter what each family calls each
/// unit and comparing strings in a translation layer, which is a branch on the
/// platform in the one place rules/rust.md forbids one.
///
/// A position the adapter's array cannot produce answers `UNSPECIFIED` rather
/// than guessing, and the contract tells a reader what to do with it: a service
/// with no entry, or an entry it cannot place, is not evidence of health or of
/// an outage. It is unreachable today — the array is a fixed size of four — and
/// it is here so that widening that array cannot silently start labelling a new
/// unit as an existing one.
#[must_use]
pub fn managed_service(position: usize) -> ManagedService {
    match position {
        0 => ManagedService::WebServer,
        1 => ManagedService::Database,
        2 => ManagedService::Cron,
        3 => ManagedService::Ssh,
        _ => ManagedService::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/monitor/managed_service_tests.rs"]
mod tests;
