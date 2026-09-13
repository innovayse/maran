//! Why a passive address was refused.

/// Rejection reasons for [`super::passive_address::PassiveAddress::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PassiveAddressError {
    /// Nothing was given.
    #[error("passive address is empty")]
    Empty,
    /// An IPv6 address was given, which `pasv_address` has no form for.
    #[error("passive address {candidate:?} is IPv6; pasv_address is IPv4-only")]
    NotIpv4 {
        /// What was given, so the operator log names it.
        candidate: String,
    },
    /// Not an IPv4 address at all — a host name, a truncated or over-long
    /// quad, an out-of-range or leading-zero octet, or a value carrying a
    /// newline or any other character that would inject a directive into
    /// `vsftpd.conf`.
    #[error("passive address {candidate:?} is not an IPv4 address")]
    Invalid {
        /// What was given, so the operator log names it.
        ///
        /// Operator-facing only. The value is a string an administrator typed
        /// into the panel and comes back to them through the panel's own
        /// role-aware error mapping; it is not a path, a version or a tool's
        /// output, so it reaches no hosting customer (rules/rust.md "Errors").
        candidate: String,
    },
}
