//! GetSftpJailStatus: does the live `sshd_config` still hold the block that
//! turns membership of the SFTP group into a jailed, shell-less login.

use maran_distro::DistroAdapter;

use crate::monitor::model::sftp_jail_status::SftpJailStatus;
use crate::monitor::monitor_error::MonitorError;
use crate::monitor::monitor_host::MonitorHost;

/// Reads the live `sshd_config` and reports whether the installer's own
/// `Match Group` block is present and intact.
///
/// # Why this exists
///
/// The installer writes this block once, when the panel is set up
/// (`installer/lib/86-sftp.sh`); nothing on the server re-checks it
/// afterwards. If a package upgrade or a hand edit removes it, every SFTP
/// login on the host silently becomes a full shell session — the group
/// membership that used to mean "chrooted, `internal-sftp` only, no
/// forwarding" now means nothing, and the panel keeps reporting every SFTP
/// account as jailed because it never looked again. This operation is that
/// look.
///
/// # What it checks, and how
///
/// It reads [`DistroAdapter::sshd_config_path`] and asks
/// [`SftpJailStatus::evaluate`] whether the exact block the installer writes —
/// found between its own marker comments — still carries `Match Group
/// <group>`, `ChrootDirectory %h`, `ForceCommand internal-sftp`,
/// `AllowTcpForwarding no` and `X11Forwarding no`. See the doc comment on
/// [`MonitorHost::read_sshd_config`] for why this reads the file rather than
/// asking sshd for its effective configuration, and for exactly what a file
/// read cannot see that a live connection could.
///
/// # Errors
///
/// Returns [`MonitorError::SshdConfigUnavailable`] when the file cannot be
/// read. **This is deliberately an error and not [`SftpJailStatus::Drifted`]:**
/// a missing or unreadable configuration is not evidence about the block, it
/// is the absence of any evidence at all, and a caller that could not read
/// the file must not report the confident, specific finding "the jail is
/// broken" over a question it never actually got to ask.
pub fn get_sftp_jail_status(
    host: &dyn MonitorHost,
    distro: &dyn DistroAdapter,
) -> Result<SftpJailStatus, MonitorError> {
    let config = host.read_sshd_config(distro.sshd_config_path())?;

    Ok(SftpJailStatus::evaluate(&config, distro.sftp_group()))
}

#[cfg(test)]
#[path = "../tests/monitor/get_sftp_jail_status_tests.rs"]
mod tests;
