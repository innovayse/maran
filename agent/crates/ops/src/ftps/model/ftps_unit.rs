//! The systemd commands this area runs against its own unit.

use maran_agent_core::agent_paths::AgentPaths;

use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The FTPS unit, and the four argument vectors the area ever hands the service
/// manager for it.
///
/// Every vector ends with the end-of-options marker and then the unit name,
/// because `systemctl` parses with `getopt_long` and honours it: the unit name is
/// a constant today, and the marker keeps it from ever being read as an option if
/// a later change makes it a value.
///
/// A type holding constants rather than four string literals repeated across
/// `enable_ftps`, `disable_ftps`, `reload_ftps_tls` and `observe_ftps`: the unit
/// name appears once, and a reader looking for "everything this area asks
/// systemd to do" finds the complete list in one place. The program itself is
/// never named here — that is a platform fact and comes from
/// `DistroAdapter::service_manager()` (rules/rust.md "Distro adapter").
pub struct FtpsUnit;

impl FtpsUnit {
    /// Bring the daemon up on whatever configuration is currently on disk.
    ///
    /// `restart` and not `reload`: vsftpd has no reload signal that re-reads its
    /// configuration or its certificate material, so replacing either means
    /// stopping and starting the process.
    pub const RESTART: [&'static str; 3] = ["restart", END_OF_OPTIONS, AgentPaths::FTPS_UNIT];

    /// Ask whether the unit is active.
    ///
    /// Exits zero when it is. Asked AFTER a restart because the unit is
    /// `Type=simple`: its start succeeds as soon as the process has been forked,
    /// so the restart's own status says nothing about whether the daemon is
    /// still alive a moment later.
    pub const IS_ACTIVE: [&'static str; 3] = ["is-active", END_OF_OPTIONS, AgentPaths::FTPS_UNIT];

    /// Stop the daemon and take it out of the boot sequence.
    ///
    /// One command for both halves, because leaving a stopped unit enabled means
    /// the next reboot undoes the operator's decision.
    pub const DISABLE_NOW: [&'static str; 4] =
        ["disable", "--now", END_OF_OPTIONS, AgentPaths::FTPS_UNIT];

    /// Stop the daemon without changing whether it starts at boot.
    ///
    /// Used only on the rollback path of an enable that had no previous
    /// configuration to restore: the file that was refused stays on disk for an
    /// operator to look at, and nothing serves it.
    pub const STOP: [&'static str; 3] = ["stop", END_OF_OPTIONS, AgentPaths::FTPS_UNIT];
}
