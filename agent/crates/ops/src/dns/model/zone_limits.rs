//! The ceilings the agent enforces on a zone whatever the panel allowed.

/// Defence in depth: the panel's own cap per zone is an operator option, and this
/// is the agent's last word, so a request cannot make the root daemon render a
/// file of any size.
pub struct ZoneLimits;

impl ZoneLimits {
    /// Most records a zone may carry, apex name servers excluded.
    pub const MAX_RECORDS: usize = 5_000;

    /// Most apex name servers.
    pub const MAX_NAMESERVERS: usize = 8;

    /// Largest rendered zone file, in bytes: far above what the record cap can
    /// reach with ordinary records and below anything that stresses the checker.
    pub const MAX_RENDERED_BYTES: usize = 2 * 1024 * 1024;
}
