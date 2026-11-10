//! The state of one inventory read.

use maran_agent_core::command_outcome::CommandOutcome;

use super::model::bounded_text::BoundedText;
use super::model::inventory_limits::InventoryLimits;
use super::model::inventory_sink::InventorySink;
use super::model::inventory_step::InventoryStep;
use super::model::inventory_summary::InventorySummary;
use super::model::source_account_record::SourceAccountRecord;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use crate::transfer::{RemoteCommand, SourceShell, TransferOpError};

/// One read in progress: the shell commands run through, the sink events go to,
/// and the counts of what was sent.
///
/// Every command and every event of a read passes through here, so the two
/// rules that hold for all of them are held in one place: a command's failure
/// is translated into a warning the same way whichever step ran it, and the
/// number of warnings is bounded (after [`InventoryLimits::MAX_WARNINGS`] one
/// `Truncated` is sent and the rest are dropped, so a source built to produce
/// warnings cannot make the stream unbounded).
pub struct InventoryRun<'a> {
    /// Where commands run.
    shell: &'a dyn SourceShell,
    /// Where events go.
    sink: &'a mut dyn InventorySink,
    /// How many warnings have been sent, the bounding `Truncated` included.
    warnings_sent: u32,
    /// How many accounts have been sent.
    accounts_sent: u32,
    /// Whether the warning bound was reached and its `Truncated` sent.
    saturated: bool,
}

impl<'a> InventoryRun<'a> {
    /// Starts a read.
    pub fn new(shell: &'a dyn SourceShell, sink: &'a mut dyn InventorySink) -> Self {
        Self {
            shell,
            sink,
            warnings_sent: 0,
            accounts_sent: 0,
            saturated: false,
        }
    }

    /// Announces `step`; `false` when nobody listens any more, in which case the
    /// read ends quietly.
    pub fn begin(&mut self, step: InventoryStep) -> bool {
        if !self.sink.is_open() {
            return false;
        }
        self.sink.progress(step);
        true
    }

    /// Sends one warning, within the warning bound.
    pub fn warn(&mut self, warning: SourceWarningRecord) {
        if self.saturated {
            return;
        }
        if self.warnings_sent >= InventoryLimits::MAX_WARNINGS {
            self.saturated = true;
            self.warnings_sent += 1;
            self.sink.warning(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
            return;
        }
        self.warnings_sent += 1;
        self.sink.warning(warning);
    }

    /// Sends every warning of `warnings`, in order.
    pub fn warn_each(&mut self, warnings: Vec<SourceWarningRecord>) {
        for warning in warnings {
            self.warn(warning);
        }
    }

    /// Sends one finished account.
    pub fn send_account(&mut self, account: SourceAccountRecord) {
        self.accounts_sent += 1;
        self.sink.account(account);
    }

    /// Runs `command` and returns its outcome.
    ///
    /// A command that ran and exited non-zero is an outcome, for the caller to
    /// judge. A command that crossed its deadline or output cap is not an
    /// answer: a `Truncated` warning is sent and `None` is returned, so the read
    /// goes on without it. Any other failure — the connection broke, the key was
    /// refused, the host key changed — ends the read.
    ///
    /// # Errors
    ///
    /// Every [`TransferOpError`] the shell returns, except `OutputBound`.
    pub fn outcome(
        &mut self,
        command: &RemoteCommand,
    ) -> Result<Option<CommandOutcome>, TransferOpError> {
        match self.shell.run(command) {
            Ok(outcome) => Ok(Some(outcome)),
            Err(TransferOpError::OutputBound(_)) => {
                self.warn(SourceWarningRecord::about_nothing(
                    SourceWarningKind::Truncated,
                ));
                Ok(None)
            }
            Err(other) => Err(other),
        }
    }

    /// Runs `command` and returns its standard output within the output bound.
    ///
    /// A non-zero status sends `failure` and returns `None`. Output beyond the
    /// bound is cut back to whole lines and a `Truncated` warning is sent; the
    /// caller sees `truncated` on the text.
    ///
    /// # Errors
    ///
    /// The same as [`InventoryRun::outcome`].
    pub fn text(
        &mut self,
        command: &RemoteCommand,
        failure: SourceWarningRecord,
    ) -> Result<Option<BoundedText>, TransferOpError> {
        self.read(command, failure, |status| status == 0)
    }

    /// Runs a LISTING command — one that is a `find` over directories of both
    /// families — and returns its standard output within the output bound.
    ///
    /// `find` exits 1 when a directory it was given is missing, and each listing
    /// names the directories of both families, so a host of either family exits 1
    /// while printing everything it has. Status 0 and 1 are therefore answers;
    /// any other status (a tool that is not there, a shell that refused) sends
    /// `failure` and returns `None`. The cost is stated: a directory that exists
    /// and cannot be read looks the same as one that is missing.
    ///
    /// # Errors
    ///
    /// The same as [`InventoryRun::outcome`].
    pub fn listing(
        &mut self,
        command: &RemoteCommand,
        failure: SourceWarningRecord,
    ) -> Result<Option<BoundedText>, TransferOpError> {
        self.read(command, failure, |status| matches!(status, 0 | 1))
    }

    /// Runs `command`; an exit status `accepts` says yes to is an answer.
    fn read(
        &mut self,
        command: &RemoteCommand,
        failure: SourceWarningRecord,
        accepts: fn(i32) -> bool,
    ) -> Result<Option<BoundedText>, TransferOpError> {
        let Some(outcome) = self.outcome(command)? else {
            return Ok(None);
        };
        if !accepts(outcome.status) {
            self.warn(failure);
            return Ok(None);
        }
        let text = BoundedText::of(outcome.stdout);
        if text.truncated {
            self.warn(SourceWarningRecord::about_nothing(
                SourceWarningKind::Truncated,
            ));
        }
        Ok(Some(text))
    }

    /// What has been sent so far.
    #[must_use]
    pub fn summary(&self) -> InventorySummary {
        InventorySummary {
            account_count: self.accounts_sent,
            warning_count: self.warnings_sent,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/inventory_run_tests.rs"]
mod tests;
