//! One installed PHP version's OPcache drop-in, and what the start-up pass did.

use crate::php::PhpOpError;

/// What a pass over the installed versions did to ONE version's OPcache drop-in.
///
/// One per installed version rather than a single verdict, for the reason
/// [`super::pool_tree_outcome::PoolTreeOutcome`] gives: the versions are separate
/// php-fpm masters, and one version's refusal says nothing about the others and
/// must name the version an operator has to look at.
#[derive(Debug)]
pub struct OpcacheDropInOutcome {
    /// The PHP version this drop-in belongs to, as the adapter spells it.
    pub version: String,

    /// `Ok` when the drop-in is on disk and accepted (written, or already equal
    /// to the render), or the typed reason it is not.
    pub result: Result<(), PhpOpError>,
}
