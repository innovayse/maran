//! The [`CronHost`] that actually touches this machine.

use std::fs::{self, Permissions};
use std::io;
use std::os::unix::fs::PermissionsExt as _;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::cron::cron_error::CronError;
use crate::cron::cron_host::CronHost;
use crate::cron::crontab_spool::CrontabSpool;
use crate::cron::entry_files::EntryFiles;
use crate::cron::mint_entry_id::mint_entry_id;
use crate::cron::model::cron_entry::CronEntry;
use crate::cron::model::cron_run_record::CronRunRecord;

/// The mode the account's cron directory is created with.
///
/// `0700`: the directory holds the customer's commands and the output of the
/// last run of each. Nothing else on the host has any business reading it, and
/// the account itself is the only writer.
const CRON_DIRECTORY_MODE: u32 = 0o700;

/// The mode an entry's command file is created with.
const COMMAND_FILE_MODE: u32 = 0o600;

/// The real host: it forks for the two home-side writes and delegates the rest.
///
/// Deliberately thin. Everything the machine actually does has a unit of its
/// own — `CrontabSpool` runs `crontab(1)`, `EntryFiles` over
/// `open_cron_directory` reads what an entry owns, `mint_entry_id` mints its
/// id — and what is left here is the one thing that cannot be delegated: the
/// privilege drop, which has to happen at the call site that knows what the
/// child must do.
///
/// # The privilege split, which is the whole point of this file
///
/// **The two crontab methods run as root**, and this file delegates both to
/// `CrontabSpool`, which carries the argument: `crontab(1)` is the correct
/// writer of the spool, and the table it reads is staged in a root-owned `0700`
/// directory rather than anywhere under a customer's home.
///
/// **Everything this file WRITES under the account's home is written as the
/// account**, through [`fork_as_account`], the workspace's one privilege drop.
/// The account's cron directory is `0700` and the account owns it, so the
/// account can put a symlink at any name inside it at any moment. A root
/// process writing through such a name would write wherever it pointed; a
/// process that has dropped to the account writes only where the account could
/// already have written. There is no `chown` anywhere in this file, and no
/// branch that creates a file as root "and then fixes it up".
///
/// # Blocking
///
/// **Every method here MUST be called from `tokio::task::spawn_blocking`**, as
/// [`CronHost`] states. `write_command_file` and `remove_entry_files` fork and
/// block in `waitpid` for as long as `fork_as_account`'s two-minute ceiling
/// allows; `read_crontab` and `install_crontab` wait on a spawned program; the
/// three reads walk a directory and read a file. Any of them on a runtime
/// worker stalls every other in-flight command (rules/rust.md "Async and
/// blocking").
///
/// # The reads, stated plainly rather than implied
///
/// [`fork_as_account`] returns `Result<(), PrivError>` — an exit status and
/// nothing else — and it closes every inherited descriptor above standard error
/// before the child's work begins, so no pipe, socket or handoff file can be
/// passed in. A dropped child therefore cannot hand bytes back **with the
/// primitives `agent-core::privs` provides today**, which is why the three
/// methods that must RETURN a customer's file contents are not written inside
/// one.
///
/// That is the exact claim and it is worth not overstating: a review
/// established that a channel IS constructible, because `close_range` closes
/// descriptors and not memory mappings — a `MAP_SHARED | MAP_ANONYMOUS` region
/// made before the fork survives the sweep and stays writable after `setuid`.
/// So the honest statement is that the primitive does not exist yet, not that
/// it cannot; adding one would touch the single module in this workspace where
/// `unsafe` is permitted, and there is a real argument that a shared mapping
/// from an unprivileged child into the root parent's address space is a worse
/// surface than the read below.
///
/// The reads are done from the root side instead, by this area's own
/// `EntryFiles` over `open_cron_directory`, which together carry the refusals
/// that make reading a directory the customer owns safe — a symlink at any
/// component of the path or at the file itself, a hardlink, a FIFO, and a level
/// that is not the account's — with the argument for each. Every one of them
/// has a test that needs no root.
///
/// So the rule this area actually follows is: **a home-side operation that
/// needs nothing back drops privileges; one that must RETURN a customer's bytes
/// is a hardened root-side open.** What stays readable through the second is
/// the set of plain files the account already owns, which is what dropping to
/// the account would have allowed as well.
pub struct ProcessCronHost {
    /// Where `crontab` and the interpreter live on this family.
    distro: &'static dyn DistroAdapter,
}

impl ProcessCronHost {
    /// Creates the host against `distro`.
    ///
    /// The adapter is held rather than passed to each method because this
    /// trait's methods answer questions about files and tables, not about
    /// platforms — a `read_crontab` that took an adapter would be asking every
    /// caller to know why.
    #[must_use]
    pub fn new(distro: &'static dyn DistroAdapter) -> Self {
        Self { distro }
    }
}

impl CronHost for ProcessCronHost {
    /// Delegates to the area's one reader of the cron spool.
    fn read_crontab(&self, account: &AccountName) -> Result<Option<String>, CronError> {
        CrontabSpool::new(self.distro).read(account)
    }

    /// Delegates to the area's one writer of the cron spool.
    fn install_crontab(&self, account: &AccountName, contents: &str) -> Result<(), CronError> {
        CrontabSpool::new(self.distro).install(account, contents)
    }

    /// Delegates to the one minter and adds nothing of its own.
    fn new_entry_id(&self) -> Result<CronEntryId, CronError> {
        mint_entry_id()
    }

    /// Creates the cron directory and writes the command file, as the account.
    ///
    /// Both steps are inside one forked child: the directory is what the file
    /// goes in, and doing them in two drops would leave a window in which the
    /// directory exists with no file and no operation on its way to make one.
    fn write_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        command: &CronCommand,
    ) -> Result<(), CronError> {
        let ids = AccountIds::resolve(account)?;
        let directory = AgentPaths::account_cron_dir(account);
        let path = AgentPaths::cron_cmd_path(account, entry);
        // Composed in the PARENT, before the fork: the less the child does the
        // better (`fork_as_account`'s own contract), and this is a formatting
        // step with no reason to be on that side.
        let contents = CronEntry::file_contents(command);

        fork_as_account(&ids, || {
            fs::create_dir_all(&directory).map_err(|_| PrivError::WorkFailed)?;
            // Applied explicitly rather than left to the daemon's umask, which
            // the agent does not control: a cron directory that came out `0755`
            // is every other local user reading the customer's commands and the
            // output of their last run.
            fs::set_permissions(&directory, Permissions::from_mode(CRON_DIRECTORY_MODE))
                .map_err(|_| PrivError::WorkFailed)?;
            fs::write(&path, contents.as_bytes()).map_err(|_| PrivError::WorkFailed)?;
            fs::set_permissions(&path, Permissions::from_mode(COMMAND_FILE_MODE))
                .map_err(|_| PrivError::WorkFailed)
        })
        .map_err(|error| match error {
            PrivError::WorkFailed => CronError::EntryFileUnwritable,
            other => CronError::Privilege(other),
        })
    }

    /// Delegates to the area's one hardened read and adds nothing of its own.
    fn read_command_file(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<String>, CronError> {
        EntryFiles::of(account, entry).command()
    }

    /// Removes the entry's three files, as the account.
    fn remove_entry_files(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<(), CronError> {
        let ids = AccountIds::resolve(account)?;
        let paths = [
            AgentPaths::cron_cmd_path(account, entry),
            AgentPaths::cron_log_path(account, entry),
            AgentPaths::cron_exit_path(account, entry),
        ];

        fork_as_account(&ids, || {
            for path in &paths {
                match fs::remove_file(path) {
                    Ok(()) => {}
                    // Idempotent file by file: a deletion retried after a lost
                    // response must converge rather than fail on its own
                    // previous work.
                    Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                    Err(_) => return Err(PrivError::WorkFailed),
                }
            }

            Ok(())
        })
        .map_err(|error| match error {
            PrivError::WorkFailed => CronError::EntryFileUnremovable,
            other => CronError::Privilege(other),
        })
    }

    /// Delegates to the area's one hardened read and adds nothing of its own.
    fn read_run_record(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
    ) -> Result<Option<CronRunRecord>, CronError> {
        EntryFiles::of(account, entry).run_record()
    }

    /// Delegates to the area's one hardened read and adds nothing of its own.
    fn read_output_tail(
        &self,
        account: &AccountName,
        entry: &CronEntryId,
        max_bytes: usize,
    ) -> Result<Option<String>, CronError> {
        EntryFiles::of(account, entry).output_tail(max_bytes)
    }
}
