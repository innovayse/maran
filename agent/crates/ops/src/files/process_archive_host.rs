//! The [`ArchiveHost`] that actually touches this machine.

use std::io::Write;
use std::path::Path;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account_for_bytes::fork_as_account_for_bytes;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::available_bytes::available_bytes;
use maran_agent_core::utils::bounded_slots::BoundedSlots;
use maran_agent_core::validation::system::name::AccountName;

use crate::files::account_home::account_home;
use crate::files::archive::archive_answer::{ArchiveAnswer, MAXIMUM_ANSWER_BYTES};
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::create_in_home::create_in_home;
use crate::files::archive::extract_in_home::extract_in_home;
use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::model::extract_archive_input::ExtractArchiveInput;
use crate::files::{ArchiveHost, FilesOpError};

/// The most archive operations in flight across every account.
///
/// Each forks a child that reads and writes for as long as the archive takes,
/// holding a blocking-pool thread and a share of the disk. Four leaves room for
/// the rest of the agent's work.
const HOST_ARCHIVE_SLOTS: usize = 4;

/// The most archive operations one account may have in flight.
///
/// Two, so a customer can unpack one archive while packing another, and one
/// account cannot take the host's share.
const ACCOUNT_ARCHIVE_SLOTS: usize = 2;

/// Prefix of the temporary file a creation writes into.
///
/// Dotted and named after the product, so a leftover from a killed child is
/// recognisable as ours and is hidden from a web server that hides dotfiles.
const TEMPORARY_PREFIX: &str = ".maran-archive";

/// Forks to the account for every archive operation, within a bound.
///
/// The only implementation that touches the machine, and the smallest piece of
/// the area: it resolves the account's ids, takes a slot, forks, and treats the
/// child's answer as hostile (a tiny record decoded strictly, never trusted past
/// its tag and its length). What a reviewer of the privileged parse should read
/// is in `extract_in_home` and `create_in_home`.
///
/// The slots are the generic counting pair `BoundedSlots` already is (it counts,
/// refuses and never waits, whatever it is counting), not a copy of it: a second
/// copy would only be able to drift. It is one type shared with the browse, bulk and
/// run-by-hand bounds. The archive count is its own instance, so
/// archives and browsing do not take each other's share.
#[derive(Debug)]
pub struct ProcessArchiveHost {
    /// How many archive operations may run at once.
    slots: BoundedSlots,
}

impl ProcessArchiveHost {
    /// Creates the host with the production bounds: 2 per account, 4 per host.
    #[must_use]
    pub fn new() -> Self {
        Self {
            slots: BoundedSlots::new(HOST_ARCHIVE_SLOTS, ACCOUNT_ARCHIVE_SLOTS),
        }
    }

    /// A name for the temporary file a creation writes into.
    ///
    /// Built in the PARENT, before the fork, for the reason `ProcessFilesHost`
    /// builds its own there. Uniqueness comes from the process id, the clock and
    /// a counter; the file is created with `O_EXCL`, so a collision fails loudly.
    fn temporary_name() -> String {
        static COUNTER: AtomicU64 = AtomicU64::new(0);
        let nanoseconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_or(0, |since| since.as_nanos());
        let count = COUNTER.fetch_add(1, Ordering::Relaxed);

        format!(
            "{TEMPORARY_PREFIX}-{}-{nanoseconds}-{count}",
            std::process::id()
        )
    }

    /// Runs `work` in a child dropped to `account` and reads its answer.
    ///
    /// # Errors
    ///
    /// [`FilesOpError::Saturated`] when no slot is free, [`FilesOpError::Privilege`]
    /// for every failure of the account, the fork, the child or its answer, and
    /// whatever the child's refusal maps to.
    fn run<W>(&self, account: &AccountName, work: W) -> Result<ArchiveReport, FilesOpError>
    where
        W: FnOnce(&Path, u32) -> Result<ArchiveReport, ArchiveRefusal>,
    {
        let _slot = self.slots.try_acquire(account)?;
        let ids = AccountIds::resolve(account)?;
        let home = account_home(account);
        let uid = ids.uid();

        let bytes = fork_as_account_for_bytes(&ids, MAXIMUM_ANSWER_BYTES, |sink| {
            let answer = match work(&home, uid) {
                Ok(report) => ArchiveAnswer::Done(report),
                Err(refusal) => ArchiveAnswer::Refused(refusal),
            };
            sink.write_all(&answer.encode())
                .map_err(|_| PrivError::WorkFailed)
        })?;

        match ArchiveAnswer::decode(&bytes) {
            Some(ArchiveAnswer::Done(report)) => Ok(report),
            Some(ArchiveAnswer::Refused(refusal)) => Err(refusal.into_op_error()),
            // A child that exited successfully without a well-formed answer did
            // not do what it was forked to do.
            None => Err(FilesOpError::Privilege(PrivError::WorkFailed)),
        }
    }
}

impl Default for ProcessArchiveHost {
    fn default() -> Self {
        Self::new()
    }
}

impl ArchiveHost for ProcessArchiveHost {
    /// Extracts in a forked child that has dropped to the account.
    fn extract_as_account(
        &self,
        input: &ExtractArchiveInput,
    ) -> Result<ArchiveReport, FilesOpError> {
        self.run(&input.account, |home, uid| {
            extract_in_home(home, input, uid)
        })
    }

    /// Creates in a forked child that has dropped to the account.
    fn create_as_account(&self, input: &CreateArchiveInput) -> Result<ArchiveReport, FilesOpError> {
        let temporary = Self::temporary_name();
        self.run(input.account(), |home, uid| {
            create_in_home(home, input, &temporary, uid)
        })
    }

    /// Asks `statvfs` of `directory` itself.
    ///
    /// # Errors
    ///
    /// As [`available_bytes`].
    fn available_bytes(&self, directory: &Path) -> Result<u64, std::io::Error> {
        available_bytes(directory)
    }
}
