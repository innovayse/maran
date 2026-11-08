//! What is wrong with one DNS label.

/// The ways a single label of a record name or target can be unacceptable.
///
/// Crate-private: [`super::dns_name::DnsName`] and
/// [`super::record_name::RecordName`] each map it onto their own error.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum LabelFault {
    /// The label is empty (two dots in a row, or a leading dot).
    Empty,
    /// The label is over 63 bytes.
    TooLong,
    /// The label starts or ends with a hyphen.
    HyphenAtEdge,
    /// The label holds a character outside ASCII letters, digits, `-` and `_`.
    IllegalCharacter(char),
}
