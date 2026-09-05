//! The three files one cron entry owns, read from the root side, safely.

use std::ffi::OsStr;
use std::fs::{File, Metadata};
use std::io::{self, Read as _, Seek as _, SeekFrom};
use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::cron::cron_error::CronError;
use crate::cron::model::cron_run_record::CronRunRecord;
use crate::cron::open_cron_directory::open_cron_directory;

/// Flags an entry's own file is opened with.
///
/// `O_NONBLOCK` is what makes a FIFO left in place of a log return instead of
/// blocking a root thread in the kernel forever; the `fstat` afterwards is what
/// refuses to read it.
const ENTRY_FILE_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// The most bytes a command file may hold before it is refused.
///
/// A `CronCommand` is at most 4096 bytes and its file adds one newline, so
/// anything approaching this is a file no version of this agent wrote. Refused
/// rather than reported truncated: a shortened command shown in a listing is a
/// lie, and one compared against a new entry would report a duplicate that is
/// not one.
const COMMAND_FILE_CEILING: u64 = 8 * 1024;

/// The most bytes an exit file may be read for before it is read as a status.
///
/// The file is `echo $?` and a newline. Anything longer will not parse as a
/// status and is reported as an unknown code, which is the honest answer for a
/// file this agent did not write.
const EXIT_FILE_CEILING: u64 = 32;

/// One entry's command, output and exit files, read as the panel needs them.
///
/// # Why these reads are root's, when every write in this area is the
/// account's
///
/// [`fork_as_account`](maran_agent_core::privs::fork_as_account::fork_as_account)
/// is the workspace's one privilege drop, and it returns an exit status and
/// nothing else: no channel comes back from the child, and every inherited
/// descriptor above standard error is closed before the child's work begins. So
/// a dropped child cannot hand bytes to the daemon **with the primitives
/// `agent-core::privs` provides today**, and a read that must RETURN a
/// customer's file contents is therefore not written inside one. The writes and
/// the removals in this area do drop, because they need nothing back.
///
/// The narrow claim is the honest one and the broad one would be false: a
/// channel IS constructible, because `close_range` closes descriptors and not
/// memory mappings. What does not exist is the primitive, and adding one would
/// touch the single module in this workspace where `unsafe` is permitted. See
/// [`ProcessCronHost`](super::ProcessCronHost) for the full statement.
///
/// # What these reads defend against, and with what
///
/// The directory belongs to the account. It is `0700` and the account owns it,
/// so between any two syscalls the agent makes, the account can replace any
/// name inside it with something else. Four things a customer can leave there
/// would otherwise turn a read into an attack on the root daemon, and each has
/// its own refusal:
///
/// - **A symlink**, at ANY component or at the file. `O_NOFOLLOW` refuses only
///   the trailing component of a path, so opening
///   `/home/<account>/.maran/cron` in one call would follow a symlink planted
///   at `.maran` — measured, not assumed. The descent is therefore one
///   component at a time from the home downwards, each level reached with
///   `openat` and `O_NOFOLLOW`, which is what makes the flag cover every level
///   instead of the last. It is the descent
///   `ops::files::open_parent_directory` performs, for the same reason.
/// - **A hardlink** to somebody else's file. It is not a symlink and it really
///   is inside the home, so every path check ever written passes it. Only the
///   inode gives it away: the file must be owned by the account and have
///   exactly one link.
/// - **A FIFO.** Opening one with no writer blocks in the kernel forever, and
///   it is not a symlink, so `O_NOFOLLOW` says nothing about it. `O_NONBLOCK`
///   makes the open return, and `is_file` refuses to read it.
/// - **A swapped directory**, between the two opens. The directory is opened
///   ONCE and the file is reached from that descriptor with `openat`; a
///   descriptor names an inode, and no rename moves an inode.
///
/// The account's ownership is checked on EVERY directory of the descent and on
/// the file, because they answer different questions: a level that is not the
/// account's is a broken or tampered-with host, while a file that is not the
/// account's inside a directory that is, is somebody linking to something they
/// should not reach. Ownership is a second answer here rather than the only
/// one — an earlier version of this file relied on it alone while its comment
/// claimed `O_NOFOLLOW` covered the path, which is the worse of the two
/// failures: a defence the next reader is free to move because the doc credits
/// something else with the work.
///
/// **Every read is bounded DURING the read and not before it.** The ceiling is
/// enforced by `Read::take`, never by trusting what the `fstat` reported: the
/// account can grow the file between the two calls, so a budget checked before
/// the read is a budget an attacker chooses the moment to exceed. What comes
/// back is the END of the file, because the interesting part of a failed run's
/// output is the error it ended with; for a file within the ceiling the tail is
/// the whole file.
///
/// What is left readable through this type is therefore the set of plain files
/// the account already owns — which is what dropping to the account would have
/// allowed as well.
pub(crate) struct EntryFiles<'a> {
    /// The account whose home the files live in, and whose uid owns them.
    account: &'a AccountName,
    /// The entry whose three files are meant.
    entry: &'a CronEntryId,
}

impl<'a> EntryFiles<'a> {
    /// The three files belonging to `entry` of `account`.
    pub(crate) fn of(account: &'a AccountName, entry: &'a CronEntryId) -> Self {
        Self { account, entry }
    }

    /// The entry's command, verbatim, or `None` when there is no such file.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::EntryFileUnreadable`] when the file is there and is
    /// larger than any this agent writes, or is not a plain file the account
    /// owns; and [`CronError::Privilege`] when the account cannot be resolved.
    pub(crate) fn command(&self) -> Result<Option<String>, CronError> {
        let path = AgentPaths::cron_cmd_path(self.account, self.entry);
        let Some(contents) = self.read(&path, COMMAND_FILE_CEILING)? else {
            return Ok(None);
        };

        // Decided on what the read actually took, never on the `fstat` before
        // it. The account owns the file and can grow it in between, so a
        // comparison against the earlier length would pass for a file that has
        // since become far larger — and hand back the truncated command this
        // ceiling exists to refuse.
        if contents.saturated {
            return Err(CronError::EntryFileUnreadable);
        }

        Ok(Some(contents.text))
    }

    /// What the entry's last run reported, or `None` when it has never run.
    ///
    /// Both halves come from the one exit file: its CONTENT is the status and
    /// its MTIME is when the run finished.
    ///
    /// # Errors
    ///
    /// As [`Self::command`], minus the size refusal — a file too long to be a
    /// status simply does not parse as one.
    pub(crate) fn run_record(&self) -> Result<Option<CronRunRecord>, CronError> {
        let path = AgentPaths::cron_exit_path(self.account, self.entry);
        let Some(contents) = self.read(&path, EXIT_FILE_CEILING)? else {
            return Ok(None);
        };

        Ok(Some(CronRunRecord {
            // Anything that is not a status reads as "unknown" rather than as a
            // failure: inventing a code for it would be indistinguishable from
            // a real one.
            exit_code: contents.text.trim().parse::<i32>().ok(),
            ran_at: contents
                .metadata
                .modified()
                .map_err(|_| CronError::EntryFileUnreadable)?,
        }))
    }

    /// The last `max_bytes` the entry's last run printed, or `None` when it has
    /// never run.
    ///
    /// An empty string is the different answer: it ran and said nothing.
    ///
    /// # Errors
    ///
    /// As [`Self::run_record`].
    pub(crate) fn output_tail(&self, max_bytes: usize) -> Result<Option<String>, CronError> {
        let path = AgentPaths::cron_log_path(self.account, self.entry);
        let ceiling = u64::try_from(max_bytes).unwrap_or(u64::MAX);

        Ok(self.read(&path, ceiling)?.map(|contents| contents.text))
    }

    /// Reads one of the three files, resolving the account's identity first.
    ///
    /// # Errors
    ///
    /// Returns [`CronError::Privilege`] when the account cannot be resolved,
    /// and [`CronError::EntryFileUnreadable`] as [`read_entry_file`] does.
    fn read(&self, path: &Path, ceiling: u64) -> Result<Option<FileContents>, CronError> {
        // Resolved at the moment of use and never cached: an account deleted
        // and recreated gets a different uid, and a stale one would authorise a
        // read of whoever now holds it.
        let ids = AccountIds::resolve(self.account)?;
        let name = path.file_name().ok_or(CronError::EntryFileUnreadable)?;
        // The HOME, not the cron directory: the levels between the two are
        // components the account can replace, so they belong inside the
        // descent rather than inside a path handed to one `open`.
        let home = PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(self.account.as_str());

        read_entry_file(&home, name, ids.uid(), ceiling)
    }
}

/// One entry file's bytes, and what the read learned about the file itself.
///
/// A named struct rather than a tuple because the third field is the one a
/// caller is most likely to forget, and forgetting it is how a truncated
/// command file gets reported as a whole one.
#[derive(Debug)]
struct FileContents {
    /// What was read, decoded lossily.
    text: String,
    /// The `fstat` taken on the descriptor before the read.
    metadata: Metadata,
    /// Whether the read stopped at the ceiling rather than at end of file.
    ///
    /// The honest form of "is this file too big", and the only one that
    /// survives the account growing the file between the `fstat` and the read.
    saturated: bool,
}

/// Reads the last `ceiling` bytes of `name` inside `home`'s cron directory.
///
/// The four refusals the type above documents all live here. The text is
/// decoded lossily — the bytes are whatever a customer's command printed, and a
/// program that emits one invalid sequence must not make its own output
/// unreadable.
///
/// `home` and `uid` are parameters rather than derived here, and that is what
/// makes every refusal testable without root: a test owns a temporary directory
/// and runs as its own uid, which is the same relationship a customer has with
/// their home. Taking the HOME rather than the cron directory is what puts the
/// `.maran` level inside the tested descent. It is the same split
/// `ops::sites`' `follow_as` uses, for the same reason.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when a directory of the descent
/// or the file is there and is not what it must be, or when the read itself
/// fails. A directory or a file that simply does not exist is `Ok(None)`: an
/// account that has never had an entry, and an entry that has never run, are
/// answers rather than failures.
fn read_entry_file(
    home: &Path,
    name: &OsStr,
    uid: u32,
    ceiling: u64,
) -> Result<Option<FileContents>, CronError> {
    let Some(directory) = open_cron_directory(home, uid)? else {
        return Ok(None);
    };

    let file = match open_in_directory(&directory, name, ENTRY_FILE_FLAGS) {
        Ok(file) => file,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(_) => return Err(CronError::EntryFileUnreadable),
    };

    let metadata = file
        .metadata()
        .map_err(|_| CronError::EntryFileUnreadable)?;
    // `is_file` is the FIFO check, the device check and the directory check all
    // three; `nlink == 1` is the hardlink check.
    if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
        return Err(CronError::EntryFileUnreadable);
    }

    let (text, saturated) = read_tail(file, metadata.len(), ceiling)?;

    Ok(Some(FileContents {
        text,
        metadata,
        saturated,
    }))
}

/// Reads at most `ceiling` bytes from the end of `file`, decoding lossily, and
/// reports whether the ceiling is what stopped it.
///
/// `length` is what the `fstat` reported and is used only to decide where to
/// start; the ceiling itself is enforced by `take`, which is what makes a file
/// grown between the two calls harmless. A file grown between the `fstat` and
/// the seek therefore yields its HEAD rather than its tail — bounded and
/// harmless, and said here rather than left to surprise a reader of
/// [`EntryFiles::output_tail`]'s "the end of the file".
///
/// The saturation flag is the answer to "was there more", taken from what was
/// actually read rather than from a length measured before the read.
///
/// # Errors
///
/// Returns [`CronError::EntryFileUnreadable`] when the seek or the read fails.
fn read_tail(mut file: File, length: u64, ceiling: u64) -> Result<(String, bool), CronError> {
    if let Some(skip) = length.checked_sub(ceiling)
        && skip > 0
    {
        file.seek(SeekFrom::Start(skip))
            .map_err(|_| CronError::EntryFileUnreadable)?;
    }

    let mut buffer = Vec::new();
    file.take(ceiling)
        .read_to_end(&mut buffer)
        .map_err(|_| CronError::EntryFileUnreadable)?;

    // From what was read, not from the `fstat`: the account owns the file and
    // can grow it between the two, so a size measured before the bytes were
    // taken is a size an attacker chooses the moment to invalidate.
    let saturated = buffer.len() as u64 >= ceiling;

    Ok((String::from_utf8_lossy(&buffer).into_owned(), saturated))
}

#[cfg(test)]
#[path = "../tests/cron/entry_files_tests.rs"]
mod tests;
