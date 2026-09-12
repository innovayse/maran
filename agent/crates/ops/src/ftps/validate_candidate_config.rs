//! The first of the two validation layers: does the daemon accept this text at
//! all, before anything on the host is replaced.

use std::time::Duration;

use maran_distro::adapter::DistroAdapter;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::candidate_outcome::CandidateOutcome;

/// How long a candidate daemon has to fall over before it is taken as accepted.
///
/// Measured rather than guessed: on both families a refusal is immediate — the
/// configuration is parsed and the socket bound before anything else happens —
/// and a good standalone run was still up after two seconds. 1500 ms is
/// comfortably past every observed refusal and short enough that a caller
/// waiting on an enable does not notice it.
const CANDIDATE_DEADLINE: Duration = Duration::from_millis(1500);

/// Runs vsftpd against `contents` in standalone mode on a loopback port, and
/// refuses the configuration if the daemon does not stay up.
///
/// # Why the daemon and not a parser
///
/// vsftpd has no `-t`. There is no mode in which it reads a file, reports on it
/// and exits zero, so the only judge of this text is the daemon, and the only
/// answer it gives is whether it lives. Everything else that could be read
/// instead was measured on Ubuntu 24.04 and AlmaLinux 9 on 2026-09-08 and found
/// to be a blind gate:
///
/// - **The exit code is only a signal in standalone mode.** In inetd mode
///   (`listen=NO`) a perfectly good configuration also exits 2, so a check that
///   ran the daemon as configured and read its status would refuse every valid
///   file. Hence the `-olisten=YES`.
/// - **`background` differs by family.** The RHEL family defaults it to `YES`, so
///   without `-obackground=NO` a good configuration exits 0 there by forking, and
///   the check would be reading the status of a process that is not the daemon.
/// - **The Debian family prints nothing.** Every refusal it has — a bad boolean,
///   an unknown key, an unloadable certificate, a bound port — exits 2 with
///   completely empty output, while the RHEL family prints a `500 OOPS:` line for
///   each. So a gate that grepped the output for a known message would pass every
///   Debian configuration, and one that required a good-case message would fail
///   every Debian configuration. Neither can see anything. This is why the output
///   is CARRIED and never interpreted, and why the refusal says outright when the
///   platform gave no reason.
///
/// # What this layer cannot see
///
/// It runs a daemon bound to loopback on a port of its own, so it says nothing
/// about the real control port being free, about the real unit starting, or about
/// a certificate whose key does not match being usable in a handshake. Those are
/// the second layer's, after the swap.
///
/// **Its own blind spot, stated:** the port comes from binding `127.0.0.1:0`,
/// reading the assigned number and dropping the socket, so in the microseconds
/// between the drop and the spawn something else can take it. The candidate is
/// then refused for a reason that is not its own. That fails safe — nothing has
/// been swapped — and reaches the operator as a refusal they can retry.
///
/// # Errors
///
/// Returns [`FtpsError::ConfigRejected`] when the daemon exited before the
/// deadline, carrying whatever it printed and a flag saying when that was empty
/// because this platform's build prints nothing. Returns
/// [`FtpsError::SpawnFailed`] when the loopback port could not be obtained or the
/// daemon could not be started at all — an operator installing a missing package
/// and one fixing a refused configuration are doing different work.
pub fn validate_candidate_config(
    host: &dyn FtpsHost,
    distro: &dyn DistroAdapter,
    contents: &str,
) -> Result<(), FtpsError> {
    let port = host.ephemeral_port()?;
    let listen_port = format!("-olisten_port={port}");
    let arguments = [
        // Stay in the foreground, so "still running" is a fact about the daemon
        // and not about a parent that forked one.
        "-obackground=NO",
        // Standalone, so the exit status means what this check reads it to mean.
        "-olisten=YES",
        "-olisten_ipv6=NO",
        // Loopback and an unused port: this must not answer a real client, and
        // must not collide with the daemon that may already be serving port 21.
        "-olisten_address=127.0.0.1",
        listen_port.as_str(),
    ];

    match host.run_candidate(
        distro.vsftpd_binary(),
        contents,
        &arguments,
        CANDIDATE_DEADLINE,
    )? {
        CandidateOutcome::StillRunning => Ok(()),
        CandidateOutcome::Exited { output } => Err(FtpsError::ConfigRejected {
            output_is_unavailable_on_this_platform: output.trim().is_empty(),
            output,
        }),
    }
}

#[cfg(test)]
#[path = "../tests/ftps/validate_candidate_config_tests.rs"]
mod tests;
