//! The firewall tooling.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The firewall tooling.
pub trait FirewallFacts: Send + Sync {
    /// Absolute path of the `nft` binary, for the process-execution
    /// allow-list.
    ///
    /// Every firewall operation is a call to this program: the rendered ruleset
    /// is checked with it, loaded with it, and every ban is an element added
    /// through it. Both families install it at `/usr/sbin/nft` — that is the
    /// path in the `nftables` package's own file list on both, which is the
    /// documented interface the rule under [`DistroAdapter`](crate::adapter::DistroAdapter) says to answer.
    /// The RHEL family's unit file spells the same file `/sbin/nft`, through
    /// that directory's merged-`/usr` symlink rather than as a second binary.
    fn nft_binary(&self) -> AdapterBinary;
}
