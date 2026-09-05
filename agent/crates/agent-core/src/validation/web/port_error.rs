//! Why a candidate port number was refused.

/// Reasons [`super::port::Port::parse`] refuses a candidate.
///
/// Two variants rather than one `OutOfRange`, because the two ends of the range
/// are refused for different reasons and only one of them is arithmetic.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PortError {
    /// The candidate was `0`.
    ///
    /// Zero is not a port a service listens on; in a firewall rule it reads as
    /// "any port", which is the opposite of what a caller asking to open one
    /// port meant. It is also what an absent field decodes to over the wire, so
    /// refusing it is what turns "the caller forgot to send a port" into an
    /// error instead of into a rule that matches everything.
    #[error("a port cannot be 0")]
    Zero,

    /// The candidate did not fit the 16-bit port field.
    #[error("`{value}` is above the highest port, 65535")]
    TooLarge {
        /// What was offered.
        value: u32,
    },
}
