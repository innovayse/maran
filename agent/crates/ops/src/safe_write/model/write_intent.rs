//! Whether a configuration write replaces a file or introduces a new one.

/// What a single-file write through the config-write protocol is allowed to
/// do to its target, and what besides the validator's exit status refuses it.
///
/// Crate-internal: callers choose by calling
/// [`super::super::render_validate_swap::write_config`] or
/// [`super::super::create_config::create_config`], and both hand this to the
/// one body they share, so the difference between them is stated in one type
/// rather than in two copies of the protocol.
pub(crate) enum WriteIntent<'a> {
    /// Replace whatever the target holds, or create it when it is absent. The
    /// validator's exit status is its whole verdict.
    Replace,

    /// Introduce a configuration that must not exist yet: a target that is
    /// already there is refused untouched, and a validator that exits 0 still
    /// refuses the write when its standard error contains any of
    /// `refused_warnings`.
    Create {
        /// Fragments of the validator's standard error that refuse the write,
        /// matched as plain substrings. Built by the caller, which is the only
        /// one that knows what the new file introduces into the tree — for a
        /// vhost, the nginx warning about each host name it serves.
        refused_warnings: &'a [String],
    },
}
