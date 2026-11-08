//! The files of one daemon and the checks that judge them.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::staged_file::StagedFile;

/// Everything one daemon needs to be brought to the rendered configuration: its
/// files, the check that reads them where the daemon reads them, and the class
/// that check runs under.
///
/// The check is a pure validator wherever the daemon has one (`postfix check`,
/// `doveconf -n`, `rspamadm configtest`). The key-value server has none, so its
/// check is the service manager restarting it, as the FTPS daemon's is — and that
/// is why [`Self::validator_class`] is data and not a constant.
#[derive(Debug, Clone)]
pub struct MailStage {
    /// Which daemon this stage configures.
    pub daemon: MailDaemon,
    /// The unit that runs the daemon.
    pub unit: String,
    /// The files, written as one change.
    pub files: Vec<StagedFile>,
    /// The program that judges the swapped-in files.
    pub validator_program: AdapterBinary,
    /// Its arguments.
    pub validator_arguments: Vec<String>,
    /// The deadline class of the check.
    pub validator_class: CallClass,
    /// The arguments of the command that runs after the swap, always through the
    /// service manager: `reload-or-restart` for a daemon that reads its files at a
    /// reload, `is-active` for the key-value server whose check already was the
    /// restart.
    pub reload_arguments: Vec<String>,
}
