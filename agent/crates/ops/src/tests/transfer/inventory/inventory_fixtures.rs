//! Small builders and a recording sink shared by the inventory tests.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::source_user_name::SourceUserName;

use crate::transfer::RemoteCommand;
use crate::transfer::inventory::model::inventory_sink::InventorySink;
use crate::transfer::inventory::model::inventory_step::InventoryStep;
use crate::transfer::inventory::model::passwd_entry::PasswdEntry;
use crate::transfer::inventory::model::source_account_record::SourceAccountRecord;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::model::source_warning_record::SourceWarningRecord;
use crate::transfer::inventory::model::vhost_file::VhostFile;

/// A configuration file at `path` holding `content`.
pub(crate) fn vhost_file(path: &str, content: &str) -> VhostFile {
    VhostFile {
        path: RemotePath::parse(path).unwrap(),
        content: content.to_owned(),
    }
}

/// An account named `name` whose home is `home`.
pub(crate) fn passwd_entry(name: &str, home: &str) -> PasswdEntry {
    PasswdEntry {
        name: AccountName::parse(name).unwrap(),
        home: RemotePath::parse(home).unwrap(),
    }
}

/// The kinds of `warnings`, in order.
pub(crate) fn kinds_of(warnings: &[SourceWarningRecord]) -> Vec<SourceWarningKind> {
    warnings.iter().map(SourceWarningRecord::kind).collect()
}

/// How many of `warnings` are of `kind`.
pub(crate) fn count_of(warnings: &[SourceWarningRecord], kind: SourceWarningKind) -> usize {
    warnings.iter().filter(|w| w.kind() == kind).count()
}

/// A sink that keeps everything it is sent and can stop listening.
#[derive(Default)]
pub(crate) struct RecordingSink {
    /// The steps announced, in order.
    pub(crate) steps: Vec<InventoryStep>,
    /// The accounts sent, in order.
    pub(crate) accounts: Vec<SourceAccountRecord>,
    /// The warnings sent, in order.
    pub(crate) warnings: Vec<SourceWarningRecord>,
    /// When set, the sink stops listening once this many steps were announced.
    pub(crate) closes_after_steps: Option<usize>,
}

impl InventorySink for RecordingSink {
    /// Remembers the step.
    fn progress(&mut self, step: InventoryStep) {
        self.steps.push(step);
    }

    /// Remembers the account.
    fn account(&mut self, account: SourceAccountRecord) {
        self.accounts.push(account);
    }

    /// Remembers the warning.
    fn warning(&mut self, warning: SourceWarningRecord) {
        self.warnings.push(warning);
    }

    /// Open until the configured number of steps has been announced.
    fn is_open(&self) -> bool {
        self.closes_after_steps
            .is_none_or(|limit| self.steps.len() < limit)
    }
}

/// `ReadCrontab` for the login `user`.
pub(crate) fn crontab_of(user: &str) -> RemoteCommand {
    RemoteCommand::ReadCrontab {
        user: SourceUserName::parse(user).unwrap(),
    }
}

/// `HomeDiskUsage` for `home`.
pub(crate) fn usage_of(home: &str) -> RemoteCommand {
    RemoteCommand::HomeDiskUsage {
        home: RemotePath::parse(home).unwrap(),
    }
}

/// `CountMailboxes` for `home`.
pub(crate) fn mailboxes_of(home: &str) -> RemoteCommand {
    RemoteCommand::CountMailboxes {
        home: RemotePath::parse(home).unwrap(),
    }
}

/// `ReadNginxVhostFile` for `path`.
pub(crate) fn read_nginx(path: &str) -> RemoteCommand {
    RemoteCommand::ReadNginxVhostFile {
        path: RemotePath::parse(path).unwrap(),
    }
}

/// `ReadApacheVhostFile` for `path`.
pub(crate) fn read_apache(path: &str) -> RemoteCommand {
    RemoteCommand::ReadApacheVhostFile {
        path: RemotePath::parse(path).unwrap(),
    }
}
