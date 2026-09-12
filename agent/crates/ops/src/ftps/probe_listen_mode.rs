//! Which listening socket this kernel will actually give the FTPS daemon.

use std::io::ErrorKind;

use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::listen_mode::ListenMode;

/// Decides whether this host can serve FTPS on a dual-stack socket, by binding
/// one the way the daemon would and seeing what the kernel says.
///
/// # Why a probe and not a setting
///
/// vsftpd's `listen` and `listen_ipv6` are mutually exclusive, and the dual-stack
/// one is right on every host that has IPv6 — which is nearly all of them, and is
/// what both distributions' own shipped configuration assumes. On a host where
/// IPv6 has been switched off at the kernel it is not merely suboptimal, it does
/// not bind at all, and the daemon dies at start with a message the customer
/// never sees. Asking an operator to know which of those they are on would be
/// asking them for a fact the kernel will answer in a microsecond.
///
/// # How the answer is classified, and what it does when it cannot tell
///
/// A refusal counts as "this kernel has no IPv6" only for the two errno values
/// that are evidence about address families: `EAFNOSUPPORT`, which std reports as
/// [`ErrorKind::Unsupported`], and `EADDRNOTAVAIL`, which it reports as
/// [`ErrorKind::AddrNotAvailable`]. Those are what a kernel built or booted
/// without IPv6 answers a `[::]:0` listening bind with.
///
/// Every other refusal — a permission problem, a resource limit, something this
/// code has not thought of — is NOT evidence about address families, and the
/// answer is the dual-stack default. That is the deliberate choice, and it is the
/// safe one for a reason worth stating: this probe is a hint, not a gate. If it
/// is wrong in the dual-stack direction the daemon's own start refuses the
/// configuration, the second validation layer sees it, and the previous
/// configuration is restored. If it were wrong in the IPv4-only direction the
/// daemon would come up perfectly and quietly serve no IPv6 client at all —
/// a failure nothing downstream can observe. So an unclassifiable error must not
/// produce the answer whose failure mode is silent.
///
/// This function cannot fail: an error is one of its two inputs.
#[must_use]
pub fn probe_listen_mode(host: &dyn FtpsHost) -> ListenMode {
    match host.bind_ipv6_listener() {
        Ok(()) => ListenMode::DualStack,
        Err(error) => match error.kind() {
            ErrorKind::Unsupported | ErrorKind::AddrNotAvailable => ListenMode::Ipv4Only,
            _ => ListenMode::DualStack,
        },
    }
}

#[cfg(test)]
#[path = "../tests/ftps/probe_listen_mode_tests.rs"]
mod tests;
