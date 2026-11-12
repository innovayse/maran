//! The `header_checks` table that tells the milter who submitted a message locally.

/// The table itself, shipped byte for byte: it has no per-host value.
const TABLE: &str = include_str!("../../templates/postfix/local_submitter_headers.regexp");

/// The `header_checks` table of the `cleanup_local` service.
///
/// Postfix never shows a milter the Received line `pickup` writes, and that line
/// is the only record of the submitting uid, so the table copies the uid into the
/// `X-Maran-Local-Submitter` header the local-submission rule reads (issue #591).
pub struct PostfixLocalSubmitterHeaders;

impl PostfixLocalSubmitterHeaders {
    /// The text of the table.
    #[must_use]
    pub const fn table() -> &'static str {
        TABLE
    }
}
