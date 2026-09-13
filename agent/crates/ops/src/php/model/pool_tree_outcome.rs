//! One installed PHP version's pool tree, and what the startup pass found in it.

use crate::php::model::pool_tree_decision::PoolTreeDecision;

/// What a startup pass found in ONE installed version's php-fpm pool tree.
///
/// One of these per installed version rather than a single verdict for the
/// host, because the trees are genuinely separate: `php-fpm8.3 -t` reads
/// `8.3`'s pool directory and knows nothing of `8.2`'s, so a pool file a
/// killed write left broken under one version says nothing about the others
/// and must not be reported as if it did. A single folded verdict would also
/// hide the version an operator has to go and fix.
///
/// The validator's own text is carried rather than logged where it was
/// produced, so the caller that decides the log level also owns the line — and
/// so a test can assert on the refusal instead of on a log.
#[derive(Debug, PartialEq, Eq)]
pub struct PoolTreeOutcome {
    /// The PHP version whose pool tree this is, as the adapter spells it.
    pub version: String,

    /// What the pass decided about that tree.
    pub decision: PoolTreeDecision,

    /// The refusing command's standard error, or the empty string when nothing
    /// refused. Empty rather than an `Option` because every consumer wants a
    /// string to log, and "" is the honest rendering of "nothing to say".
    pub stderr: String,
}
