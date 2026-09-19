//! Tests for `get_sftp_jail_status`: reading the live `sshd_config` through
//! the injectable host, rather than the pure text check `sftp_jail_status_tests.rs`
//! already covers.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_sftp_jail_status;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro, rhel_distro};
use crate::monitor::{MonitorError, SftpJailStatus};

/// The exact block `render_sshd_block` writes for the Debian-family adapter's
/// group name.
const INTACT_CONFIG: &str = "\
Port 22

# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

#[test]
fn an_intact_block_reads_as_intact() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_sshd_config(INTACT_CONFIG);

    let status = get_sftp_jail_status(&host, distro()).expect("the file is readable");

    assert_eq!(status, SftpJailStatus::Intact);
}

#[test]
fn a_missing_block_reads_as_drifted() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_sshd_config("Port 22\n");

    let status = get_sftp_jail_status(&host, distro()).expect("the file is readable");

    assert!(matches!(status, SftpJailStatus::Drifted { .. }));
}

/// **The read-your-own-answer control.** The operation must ask the file at
/// the PATH the adapter names, not at one of its own choosing or a literal
/// this crate invented — otherwise a build that quietly hard-coded the wrong
/// path would pass every other test here while checking nothing about the
/// file sshd actually reads. This asserts the VALUE the fake recorded, not
/// merely that a read happened.
///
/// Both families answer the identical literal (`/etc/ssh/sshd_config`) for
/// `sshd_config_path` — confirmed by reading `debian_services::sshd_config_path`
/// and `rhel_services::sshd_config_path` — so this test cannot distinguish
/// "asked the adapter" from "happened to hard-code the one string every
/// family returns". It is run against BOTH adapters for that reason: a
/// regression that swapped the call for a literal identical to the adapter's
/// answer would still pass here. What closes that gap is the mutation testing
/// recorded for this feature, which replaces the production call with a
/// literal that DIVERGES from the adapter's answer — a literal that matched
/// would prove nothing, since it is indistinguishable from asking correctly.
#[test]
fn the_operation_asks_for_the_path_the_adapter_names_on_both_families() {
    for adapter in [distro(), rhel_distro()] {
        let host = FakeMonitorHost::from_ubuntu_captures();

        let _ = get_sftp_jail_status(&host, adapter);

        assert_eq!(
            host.sshd_config_path_requested(),
            Some(adapter.sshd_config_path().to_owned())
        );
    }
}

/// A file that cannot be read is a failure to observe, not a finding that the
/// jail is broken — see the doc comment on `get_sftp_jail_status` for why the
/// two must not be conflated.
#[test]
fn an_unreadable_config_is_an_error_and_not_a_drifted_finding() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_sshd_config();

    let result = get_sftp_jail_status(&host, distro());

    assert_eq!(result, Err(MonitorError::SshdConfigUnavailable));
}
