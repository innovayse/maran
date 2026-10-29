//! The [`BackupHost`] that really runs the archiver and the dump client.

use std::fs::File;
use std::path::Path;
use std::process::Stdio;
use std::sync::{Mutex, PoisonError};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::run_unthreaded::run_unthreaded;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroAdapter};
use rustix::io::dup;
use rustix::stdio::{dup2_stdin, stdin};

use crate::backup::archive::dump_database::dump_arguments;
use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::backup_host::BackupHost;
use crate::backup::bound_or::bound_or;
use crate::backup::list_archive_members::list_archive_members;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::model::extract_spec::ExtractSpec;
use crate::backup::run_discarding::run_discarding;

/// How long the account-side archiver may run before this agent kills it.
///
/// **Strictly below `fork_as_account`'s own patience** (120 s, a private
/// constant in `agent-core::privs`, so it cannot be named here). That outer
/// limit kills only the forked child, and the archiver is that child's child:
/// were it to fire first, `tar` and its compressor would be orphaned and
/// would go on writing into the customer's home after the agent reported
/// failure. With this deadline the inner kill fires first, takes the archiver
/// and its compressor down together as one process group, and leaves room
/// inside the outer 120 s for the 5 s reap. The consequence, stated rather
/// than hidden: **a home that takes longer than this to extract is NOT
/// restored.** The extraction is killed, the restore unwinds, and the failure
/// reaches the panel as `ARCHIVER_REFUSED_UNDER_ACCOUNT`, which it reads as
/// "the archiver refused" and not as "the deadline passed", because a typed
/// bound cannot cross `fork_as_account`'s single exit status. This is NOT the
/// twelve hours the [`CallClass::Bulk`] table promises the rest of this area.
/// The limit pre-dates this bound (the outer 120 s used to kill only the forked
/// child and orphan `tar`); lifting it means giving `fork_as_account` a
/// patience argument, which is a change to `privs/` and carries its own threat
/// note and second reviewer, so it is tracked as a separate issue.
const ACCOUNT_ARCHIVER_DEADLINE: Duration = Duration::from_secs(100);

/// The client flags that make a load read SQL and print nothing conversational.
///
/// `--batch` so the client never tries to be interactive on a pipe, and
/// `--force` is deliberately ABSENT: a dump that fails partway must stop, not
/// carry on past the statement it choked on and leave a database that is half
/// this backup and half the last one.
const CLIENT_BATCH: &str = "--batch";

/// The client flag that runs one statement given on the command line.
const CLIENT_EXECUTE: &str = "--execute";

/// The statement that drops a database if it is there.
///
/// `IF EXISTS`, because repeating an operation must converge: a restore retried
/// after a crash between a drop and its load has to be able to continue rather
/// than refuse forever. The name is interpolated as a backtick-quoted
/// identifier and it is a [`DatabaseName`], whose alphabet holds no backtick,
/// no space, no quote and no semicolon — there is nothing in it to close the
/// quoting with.
const DROP_STATEMENT: &str = "DROP DATABASE IF EXISTS";

/// The `status` reported when the archiver refused inside the unprivileged
/// child.
///
/// `fork_as_account`'s only channel out of the child is an exit status of its
/// own making, and it collapses every failure of the work closure into one code
/// — deliberately, because a richer channel from a customer's process into the
/// root parent is a surface. So the archiver's own exit status does not survive
/// the fork, and this constant stands in its place.
///
/// Negative for the reason [`PROGRAM_UNAVAILABLE`] is negative: no exit status
/// is negative, so an operator reading it knows they are looking at this
/// agent's word about the child rather than at `tar`'s. A DIFFERENT negative
/// value from `PROGRAM_UNAVAILABLE`, because the two say different things —
/// "the archiver could not be started" and "the archiver ran, as the customer,
/// and refused".
const ARCHIVER_REFUSED_UNDER_ACCOUNT: i32 = -2;

/// Serialises the window in which this process's standard input is the artifact.
///
/// [`ProcessBackupHost::extract`] hands the account's `tar` its archive by
/// putting the open artifact on descriptor 0 immediately before `fork_as_account`
/// and putting the daemon's own back immediately after — descriptor 0 is the
/// only channel across that fork, because the child closes every descriptor
/// above 2 before it runs any work.
///
/// Descriptor 0 is process-wide, and backups are serialised PER ACCOUNT
/// (the per-account lock in `crate::accounts`), so two accounts can be restored at once. Without this lock
/// the second restore's `dup2` would land inside the first one's window and one
/// customer's `tar` would be reading the other's artifact. The lock makes that
/// window one-at-a-time; it is held for a fork and a wait, which is the length
/// of one extraction.
///
/// **What the lock does NOT cover, stated rather than hidden:** any OTHER
/// `fork_as_account` child forked during the window inherits descriptor 0 as
/// well. That is harmless only because of a property this agent has and must
/// keep — every other spawn names its child's standard input explicitly
/// (`run_bounded` takes a [`ChildInput`] that has no inheriting variant;
/// `load_dump` hands it a file; `run_unthreaded` takes the `Stdio` it is given
/// and this method's account-side call is the only one that passes
/// `Stdio::inherit()`) — so no program the agent starts ever reads an
/// inherited descriptor 0. A future spawn that inherits standard input would break that,
/// and would leak one customer's archive into another customer's process.
static STDIN_IS_THE_ARTIFACT: Mutex<()> = Mutex::new(());

/// Runs the real programs against the real machine — the one place in this area
/// that spawns anything.
///
/// Deliberately the smallest piece of the area: every decision worth reviewing
/// lives in the operations and in the two argv builders, where it is tested
/// against a fake. What is left here is a spawn, a `stat` and a clock.
///
/// Both binaries are taken from the `DistroAdapter` once, at construction, and
/// stored. They are platform facts, so they come from the adapter and never
/// from a literal in this crate (rules/rust.md "Distro adapter"), and storing
/// them is what makes it impossible for the program half of an argv to be
/// influenced by anything a request carried.
pub struct ProcessBackupHost {
    /// Absolute path of `tar`.
    tar_binary: AdapterBinary,
    /// Absolute path of the compressor `tar` is told to run.
    ///
    /// Held here beside `tar`'s own path because it is the same kind of fact —
    /// a platform binary the adapter measured — and because holding it is what
    /// keeps a bare name out of every argv this host builds. It is handed to
    /// the two argv builders and used once directly, by `list_members`. It is
    /// never spawned by this agent itself: `tar` starts it.
    gzip_binary: AdapterBinary,
    /// Absolute path of the database dump client — the REAL binary on each
    /// family, never the compatibility symlink beside it.
    dump_binary: AdapterBinary,
    /// Absolute path of the database client a restore drops with and loads
    /// through. A different binary from the dump client: one writes SQL out and
    /// the other reads SQL in.
    client_binary: AdapterBinary,
}

impl ProcessBackupHost {
    /// Creates the host, taking every program path from `distro`.
    #[must_use]
    pub fn new(distro: &dyn DistroAdapter) -> Self {
        Self {
            tar_binary: distro.tar_binary(),
            gzip_binary: distro.gzip_binary(),
            dump_binary: distro.database_dump_binary(),
            client_binary: distro.mysql_client_binary(),
        }
    }
}

impl BackupHost for ProcessBackupHost {
    /// The host's wall clock, in seconds since the Unix epoch.
    ///
    /// A clock before the epoch answers 0 rather than panicking or wrapping: a
    /// host whose clock is set to 1969 is a misconfigured host, and the honest
    /// consequence is a manifest with an obviously wrong timestamp, not a root
    /// daemon that stops backing anything up.
    fn now_unix(&self) -> i64 {
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .ok()
            .and_then(|elapsed| i64::try_from(elapsed.as_secs()).ok())
            .unwrap_or_default()
    }

    /// Resolves the account out of the real password database, through
    /// `getpwnam_r`.
    ///
    /// The one implementation of this method, and it is deliberately the whole
    /// of it: the decision this seam exists for — whether the answer changed
    /// since the last time it was asked — belongs to the operation, not here.
    fn account_identity(&self, account: &AccountName) -> Result<AccountIdentity, BackupError> {
        let ids =
            AccountIds::resolve(account).map_err(|_| BackupError::ExtractionIdentityUnavailable)?;

        Ok(AccountIdentity {
            uid: ids.uid(),
            gid: ids.gid(),
        })
    }

    /// Spawns the dump client with `dump_arguments` and measures what it
    /// wrote.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse, and the dump reaches its file through the client's
    /// own `--result-file` rather than through a redirect.
    ///
    /// The client's own output is DISCARDED, never read: the child's standard
    /// output and error go to the null device. It is diagnostic only, and it is
    /// the one place a customer's rows can appear in text: a client refusing
    /// halfway prints the statement it choked on. Nothing in this area has a
    /// field to put it in ([`BackupError`]'s shape is the argument), so this
    /// process never holds a byte of it.
    ///
    /// The run is bounded: the [`CallClass::Bulk`] deadline (twelve hours,
    /// because a dump's length is the customer's database's), after which the
    /// client and its whole process group are killed and the call returns
    /// [`BackupError::Bound`]. No `--` is put before the database name: the
    /// name is a [`DatabaseName`], which is prefixed with the owning account's
    /// name and therefore cannot begin with a dash, and the dump client's
    /// handling of `--` has not been measured on both families.
    ///
    /// # Errors
    ///
    /// [`BackupError::DumpFailed`] for a client that could not be started
    /// (status `PROGRAM_UNAVAILABLE`), one that exited non-zero, and one
    /// whose result file cannot be measured afterwards.
    /// [`BackupError::Bound`] when the deadline passed.
    fn dump_database(&self, database: &DatabaseName, into: &Path) -> Result<u64, BackupError> {
        let arguments = dump_arguments(database, into);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();

        let outcome = run_discarding(
            self.dump_binary.as_path(),
            &borrowed,
            CallClass::Bulk,
            ChildInput::Null,
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::DumpFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::DumpFailed {
                status: outcome.status,
            });
        }

        into.symlink_metadata()
            .map(|metadata| metadata.len())
            .map_err(|_| BackupError::DumpFailed {
                status: PROGRAM_UNAVAILABLE,
            })
    }

    /// Spawns `tar` with [`ArchiveSpec::arguments`] and measures the artifact.
    ///
    /// The argv comes from the spec and is not assembled here, so the flags this
    /// product's safety depends on are stated once, next to the reasons for
    /// them, rather than in a spawn nothing asserts on.
    ///
    /// The run is bounded exactly as [`Self::dump_database`]'s is, and its
    /// output is discarded for the same reason. The finished archive is then
    /// listed with [`Self::list_members`]'s bounds, so an archive restore would
    /// refuse is refused here instead. **No `--` is put in this argv,
    /// and it must never be:** GNU `tar` takes `-C` positionally among its
    /// operands, so a `--` ahead of the member operands would turn the second
    /// `-C` into a member name.
    ///
    /// # Errors
    ///
    /// [`BackupError::ArchiveFailed`], as [`Self::dump_database`] reports its
    /// own three cases, and [`BackupError::Bound`] when the deadline passed.
    fn create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError> {
        let arguments = spec.arguments(self.gzip_binary.as_str());
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();

        let outcome = run_discarding(
            self.tar_binary.as_path(),
            &borrowed,
            CallClass::Bulk,
            ChildInput::Null,
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::ArchiveFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::ArchiveFailed {
                status: outcome.status,
            });
        }

        let bytes = spec
            .artifact
            .symlink_metadata()
            .map(|metadata| metadata.len())
            .map_err(|_| BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            })?;

        // Restore refuses a listing past `MAXIMUM_MEMBERS` / `LISTING_STDOUT_CAP`
        // (see `list_members`), and the size ceiling `CreateBackup` applies
        // counts bytes and not members. Listing the archive just written, with
        // the SAME bounds restore will use, makes creation refuse at the figure
        // restore refuses at: a backup that cannot be restored is never
        // reported as created. This is the AUTHORITY; `create_backup` already
        // refused a home whose walk counted more members than the ceiling
        // before any work began, so this listing only fails at the end for a
        // home near the ceiling or for a listing past the byte cap.
        self.list_members(&spec.artifact)?;

        Ok(bytes)
    }

    /// Lists `artifact`'s members with `tar --list`, extracting nothing.
    ///
    /// Delegates to `list_archive_members`, which owns the bounds (deadline,
    /// output caps, member count, name length) and the rule that a bound refuses
    /// the archive and never truncates the listing.
    ///
    /// # Errors
    ///
    /// As `list_archive_members`: [`BackupError::ArchiveFailed`] or
    /// [`BackupError::Bound`].
    fn list_members(&self, artifact: &Path) -> Result<Vec<String>, BackupError> {
        list_archive_members(&self.tar_binary, &self.gzip_binary, artifact)
    }

    /// Extracts one member, as root or as the account, exactly as the spec's
    /// own [`ExtractSpec::identity`] says — reading the artifact from a
    /// descriptor this host opens, never from a path the archiver is given.
    ///
    /// The identity is READ from the spec and never chosen here. That is the
    /// point of deriving it from the member: an implementation that looked at
    /// what it was extracting and picked an identity would be a second place
    /// R3 and R4 have to be right, and the wrong answer there is root
    /// unpacking a customer's archive into a customer's home.
    ///
    /// For the account's half, the spawn happens INSIDE `fork_as_account`, so
    /// `tar` itself runs at the customer's uid and every file it creates is
    /// theirs by construction rather than by a `chown` afterwards. The argv is
    /// built in the parent, because the smallest child is the best child
    /// (`fork_as_account`'s own contract).
    ///
    /// # How the artifact reaches a process that may not open it
    ///
    /// The artifact is `0600` inside a `0700` root-owned directory, and the
    /// account's `tar` runs as the customer. It is opened HERE, as root, and
    /// reaches the archiver as an open file — `--file -`, exactly as
    /// [`Self::load_dump`] feeds a dump to the client. The customer's `tar`
    /// reads a descriptor it could never have opened itself, and the artifact's
    /// mode is not touched.
    ///
    /// Crossing the fork is what forces descriptor 0 specifically:
    /// `fork_as_account`'s child closes every inherited descriptor above 2
    /// before it runs the work, so an ordinary `Stdio::from(file)` would be
    /// handing the child a number that no longer names anything. The artifact
    /// is therefore `dup2`ed onto this process's own standard input for the
    /// length of the fork, under `STDIN_IS_THE_ARTIFACT`, and the daemon's
    /// own standard input is put back before this method returns — on the
    /// failure paths too. The root-side extractions need none of that: nothing
    /// is forked, so the open file is passed straight to the child.
    ///
    /// # Deadlines, and what a kill reaches
    ///
    /// Root's extractions (the manifest and the dumps) run for at most the
    /// [`CallClass::Bulk`] deadline. The account's half runs for at most
    /// `ACCOUNT_ARCHIVER_DEADLINE`, inside its own process group, so that the
    /// kill takes `tar` and its compressor down together before
    /// `fork_as_account`'s own, shorter-reaching, limit can fire. **That is
    /// 100 s, not twelve hours:** a home whose extraction takes longer is not
    /// restored, and the failure reads as an archiver refusal (see
    /// `ACCOUNT_ARCHIVER_DEADLINE`). Both starts
    /// use the core's unthreaded path: no thread is started here, and the
    /// forked child of a multi-threaded process must not start one.
    ///
    /// # Errors
    ///
    /// [`BackupError::ArchiveFailed`] when the artifact cannot be OPENED
    /// (`PROGRAM_UNAVAILABLE`), when the archiver refuses or cannot be run,
    /// and — for the account's half — when the archiver refused inside the
    /// child (`ARCHIVER_REFUSED_UNDER_ACCOUNT`, because the child's exit
    /// status does not survive `fork_as_account`).
    ///
    /// [`BackupError::Bound`] when a root-side extraction passed its deadline.
    /// An account-side archiver that passed its deadline is reported as
    /// `ARCHIVER_REFUSED_UNDER_ACCOUNT`, because the only channel out of the
    /// forked child is one exit status of `fork_as_account`'s own making and a
    /// typed bound cannot cross it.
    ///
    /// [`BackupError::ExtractionIdentityUnavailable`] ONLY when the account
    /// really could not be entered: it could not be resolved, the drop
    /// syscalls failed or did not verify, no child could be forked, or the
    /// child could not be waited for. An archiver that ran as the account and
    /// refused is NOT this variant — that distinction is the whole reason this
    /// method matches on `PrivError` instead of discarding it.
    fn extract(&self, spec: &ExtractSpec) -> Result<(), BackupError> {
        let arguments = spec.arguments(self.gzip_binary.as_str());
        let artifact = File::open(&spec.artifact).map_err(|_| BackupError::ArchiveFailed {
            status: PROGRAM_UNAVAILABLE,
        })?;

        match spec.identity() {
            ExtractIdentity::Root => {
                let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
                run_archiver(
                    self.tar_binary.as_path(),
                    &borrowed,
                    Stdio::from(artifact),
                    CallClass::Bulk.deadline(),
                )
            }
            ExtractIdentity::Account(account) => {
                let ids = AccountIds::resolve(&account)
                    .map_err(|_| BackupError::ExtractionIdentityUnavailable)?;
                let program = self.tar_binary.clone();

                // Poison recovery rather than propagation, as the per-account lock
                // argues for its own registry: this lock guards a window, not
                // an invariant, and the window is closed by the time any panic
                // could be observed here.
                let _window = STDIN_IS_THE_ARTIFACT
                    .lock()
                    .unwrap_or_else(PoisonError::into_inner);

                // Taken BEFORE the substitution, and the extraction is refused
                // rather than attempted if it cannot be taken: a run that could
                // not put the daemon's standard input back would leave every
                // later spawn of this process reading a customer's archive.
                let daemon_stdin = dup(stdin()).map_err(|_| BackupError::ArchiveFailed {
                    status: PROGRAM_UNAVAILABLE,
                })?;
                dup2_stdin(&artifact).map_err(|_| BackupError::ArchiveFailed {
                    status: PROGRAM_UNAVAILABLE,
                })?;

                let extracted = fork_as_account(&ids, || {
                    let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
                    // `Stdio::inherit()` and not a file: the child holds the
                    // artifact on descriptor 0 and holds no other descriptor at
                    // all.
                    run_archiver(
                        program.as_path(),
                        &borrowed,
                        Stdio::inherit(),
                        ACCOUNT_ARCHIVER_DEADLINE,
                    )
                    .map_err(|_| PrivError::WorkFailed)
                });

                let restored = dup2_stdin(&daemon_stdin);
                drop(artifact);

                match extracted {
                    // The child's work failed, and `fork_as_account` reports
                    // exactly one thing that means that: the archiver refused.
                    // Reporting it as an identity failure — which this did, and
                    // which cost a diagnosing session most of its time — says
                    // the account could not be entered when the account WAS
                    // entered and `tar` said no.
                    Err(PrivError::WorkFailed) => Err(BackupError::ArchiveFailed {
                        status: ARCHIVER_REFUSED_UNDER_ACCOUNT,
                    }),
                    Err(_) => Err(BackupError::ExtractionIdentityUnavailable),
                    Ok(()) => restored.map_err(|_| BackupError::ArchiveFailed {
                        status: PROGRAM_UNAVAILABLE,
                    }),
                }
            }
        }
    }

    /// Drops `database` through the database client, over the local socket.
    ///
    /// The agent holds no database credential: it connects as `root@localhost`
    /// and is authenticated by the uid doing the connecting, which is the same
    /// arrangement the database area already relies on.
    ///
    /// The run is bounded by the [`CallClass::Bulk`] deadline and not the
    /// database class's, and its output is discarded. This statement is a
    /// restore's point of no return (`replace_database`): it drops a database of
    /// up to the dump ceiling's size, and a client killed at the shorter
    /// deadline does not stop a drop the server has already begun. That would
    /// leave the half-dropped state the restore's recovery exists for, so the
    /// long deadline is what keeps the kill rare, and a kill that does happen
    /// reaches the caller as [`BackupError::Bound`], which `restore_backup`
    /// answers with the same unwind it uses for any failure after the drop. The
    /// statement is not given a server-side lock wait limit here: a drop that
    /// waits on a customer's open transaction is stopped by the agent killing
    /// the client at the deadline, but the server may keep waiting for it (a
    /// residual recorded in the threat note).
    ///
    /// # Errors
    ///
    /// [`BackupError::DropFailed`], and [`BackupError::Bound`] when the
    /// deadline passed.
    fn drop_database(&self, database: &DatabaseName) -> Result<(), BackupError> {
        let statement = format!("{DROP_STATEMENT} `{}`", database.as_str());
        let borrowed = [CLIENT_BATCH, CLIENT_EXECUTE, statement.as_str()];

        let outcome = run_discarding(
            self.client_binary.as_path(),
            &borrowed,
            CallClass::Bulk,
            ChildInput::Null,
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::DropFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;
        if outcome.status != 0 {
            return Err(BackupError::DropFailed {
                status: outcome.status,
            });
        }

        Ok(())
    }

    /// Loads the dump at `from` by handing the client the file as its standard
    /// input, with no default database on the connection.
    ///
    /// The file is opened here and passed as a descriptor rather than read into
    /// memory: a dump is a customer's whole database and can be gigabytes, and
    /// a root daemon that reads one whole is a root daemon the kernel kills
    /// (rules/rust.md, "never read a customer file into memory whole"). There
    /// is no shell and therefore no redirect (rules/security.md item 3).
    ///
    /// **The client's own output is never captured: both streams are the
    /// null device, so the kernel discards it and this process never holds a
    /// byte of it.** The dump, the archive and the drop work the same way
    /// (`ChildOutput::Discard`).
    ///
    /// The run is bounded by the [`CallClass::Bulk`] deadline (a dump is a
    /// customer's whole database), after which the client and its process
    /// group are killed.
    ///
    /// The reason there is nothing to capture is why the null device is the
    /// right place for it: this is the one place a
    /// customer's rows can appear as text — a client refusing halfway prints the
    /// statement it choked on — and nothing in this area has a field to put it
    /// in. Not reading it at all means the rows are never in this root process's
    /// address space, so no later change can log what was never held. What is
    /// lost is the diagnostic, and the exit status in
    /// [`BackupError::LoadFailed`] is what an operator gets instead.
    ///
    /// # Errors
    ///
    /// [`BackupError::LoadFailed`], including for a dump file that cannot be
    /// opened, and [`BackupError::Bound`] when the deadline passed.
    fn load_dump(&self, _database: &DatabaseName, from: &Path) -> Result<(), BackupError> {
        let dump = File::open(from).map_err(|_| BackupError::LoadFailed {
            status: PROGRAM_UNAVAILABLE,
        })?;

        // The database is NOT named on the command line, and that is a
        // correction rather than an omission. It used to be, to make a dump
        // that somehow carried no `CREATE DATABASE` of its own fail rather
        // than land in whatever database the client last had open — but the
        // one caller that matters is a RESTORE, which loads this dump
        // immediately after dropping that very database. A client told to open
        // a database that does not exist refuses at connect time, before it
        // reads a byte of the dump, so naming it made every restore fail and
        // then made its own rollback fail the same way. Measured on a real
        // host: exit 1 for both the archive's dump and the rollback dump.
        //
        // What the name was buying is bought anyway. The dump is taken with
        // `--databases`, so it carries its own `CREATE DATABASE` and `USE`; a
        // dump that carried neither would run with NO default database
        // selected and its first statement would be refused for exactly that
        // reason. The bad outcome the argument was written against — landing
        // silently in some other database — is not reachable from a connection
        // that has no default database at all.
        let outcome = run_discarding(
            self.client_binary.as_path(),
            &[CLIENT_BATCH],
            CallClass::Bulk,
            ChildInput::File(dump),
        )
        .map_err(|error| {
            bound_or(
                error,
                BackupError::LoadFailed {
                    status: PROGRAM_UNAVAILABLE,
                },
            )
        })?;

        if outcome.status != 0 {
            return Err(BackupError::LoadFailed {
                status: outcome.status,
            });
        }

        Ok(())
    }
}

/// Spawns the archiver with `arguments`, reading its archive from `archive`,
/// and turns its exit into this area's error.
///
/// Free-standing rather than a method, because it is called from inside a
/// forked child where `self` is a reference into a process that no longer has
/// the parent's threads. It takes the program, the argv, the descriptor the
/// archive arrives on and the deadline, and nothing else.
///
/// **Not `run_bounded`, and the reason is the fork:** `run_bounded` starts a
/// reader thread per captured stream, and a thread started in the child of a
/// fork of a multi-threaded process is a thread holding locks no other thread
/// will release. [`run_unthreaded`] starts none: it nulls both output streams
/// and polls for the exit. It also takes the standard input as the `Stdio` it
/// is given, which is what an archive arriving on a descriptor needs. The
/// environment is still the core's cleared one, so no `TAR_OPTIONS` or
/// `LD_PRELOAD` from the unit reaches the archiver, and there is no shell at
/// any point (rules/security.md item 3).
///
/// The archiver runs in a process group of its own, and the group is killed at
/// `deadline`. That is what reaches the compressor `tar` starts, which a kill
/// of `tar`'s pid alone would orphan.
///
/// Both output streams are the null device. There is nothing in
/// [`BackupError`] to carry a diagnostic, and this half of the area reads a
/// CUSTOMER-supplied archive: a `tar` that refuses prints the member name it
/// choked on, which is attacker-chosen text. Not reading it means it is never
/// in this root process's address space.
///
/// # Errors
///
/// [`BackupError::ArchiveFailed`], with [`PROGRAM_UNAVAILABLE`] when the
/// archiver could not be started or was killed by a signal the core did not
/// send, and [`BackupError::Bound`] when `deadline` passed.
fn run_archiver(
    program: &Path,
    arguments: &[&str],
    archive: Stdio,
    deadline: Duration,
) -> Result<(), BackupError> {
    let code = run_unthreaded(program, arguments, archive, deadline).map_err(|error| {
        bound_or(
            error,
            BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            },
        )
    })?;
    if code != 0 {
        return Err(BackupError::ArchiveFailed { status: code });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/process_backup_host_tests.rs"]
mod tests;
