//! The RHEL-family adapter implementation.

use crate::DistroAdapter;
use crate::family::DistroFamily;

/// Implements the agent's operations the RHEL way: dnf, `conf.d`, `nginx` user,
/// SELinux contexts. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct RhelAdapter;

impl DistroAdapter for RhelAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Rhel
    }

    fn nologin_shell(&self) -> &'static str {
        // The path RHEL documents. /usr/sbin/nologin also resolves on RHEL 8 and later
        // through the merged-/usr symlink, but a symlink that happens to exist is not a
        // contract — the documented path is.
        "/sbin/nologin"
    }
}
