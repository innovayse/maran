//! The RHEL-family adapter implementation.

use crate::DistroAdapter;
use crate::family::DistroFamily;

/// Implements the agent's operations the RHEL way: dnf, `conf.d`, `nginx` user,
/// SELinux contexts. Stateless, so [`crate::adapter_for`] can hand out one shared
/// reference.
pub struct RhelAdapter;

impl DistroAdapter for RhelAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Rhel
    }
}
