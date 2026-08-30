//! The Debian-family adapter implementation.

use crate::DistroAdapter;
use crate::family::DistroFamily;

/// Implements the agent's operations the Debian way: apt, `sites-available`,
/// `www-data`. Stateless, so [`crate::adapter_for()`] can hand out one shared
/// reference.
pub struct DebianAdapter;

impl DistroAdapter for DebianAdapter {
    fn family(&self) -> DistroFamily {
        DistroFamily::Debian
    }

    fn nologin_shell(&self) -> &'static str {
        "/usr/sbin/nologin"
    }
}
