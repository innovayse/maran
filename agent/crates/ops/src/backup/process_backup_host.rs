//! The [`BackupHost`] that really runs the archiver and the dump client.

use std::fs::File;
use std::path::Path;
use std::process::{Command, Stdio};
use std::sync::{Mutex, PoisonError};
use std::time::{SystemTime, UNIX_EPOCH};

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::apply_child_environment::apply_child_environment;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_distro::DistroAdapter;
use rustix::io::dup;
use rustix::stdio::{dup2_stdin, stdin};

use crate::backup::archive::dump_database::dump_arguments;
use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::model::extract_spec::ExtractSpec;

/// The flag that lists an archive's members without extracting anything.
const LIST: &str = "--list";

/// The flag that names the decompressor, by absolute path.
///
/// The same spelling the two argv builders use, and here for the same reason:
/// `--gzip` would have `tar` resolve a bare `gzip` through `PATH` in a child of
/// this root daemon. `--list` reads a customer-supplied artifact, so it gets
/// the same treatment as the extraction that follows it.
const COMPRESS_PROGRAM: &str = "--use-compress-program=";

/// The flag naming the archive file to read.
const FILE: &str = "--file";

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
/// (`backup_lock`), so two accounts can be restored at once. Without this lock
/// the second restore's `dup2` would land inside the first one's window and one
/// customer's `tar` would be reading the other's artifact. The lock makes that
/// window one-at-a-time; it is held for a fork and a wait, which is the length
/// of one extraction.
///
/// **What the lock does NOT cover, stated rather than hidden:** any OTHER
/// `fork_as_account` child forked during the window inherits descriptor 0 as
/// well. That is harmless only because of a property this agent has and must
/// keep — every other spawn either nulls its child's standard input
/// (`spawn_argv`, which is all of them) or names it explicitly (`load_dump`,
/// this method) — so no program the agent starts ever reads an inherited
/// descriptor 0. A future spawn that inherits standard input would break that,
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
    tar_binary: String,
    /// Absolute path of the compressor `tar` is told to run.
    ///
    /// Held here beside `tar`'s own path because it is the same kind of fact —
    /// a platform binary the adapter measured — and because holding it is what
    /// keeps a bare name out of every argv this host builds. It is handed to
    /// the two argv builders and used once directly, by `list_members`.
    gzip_binary: String,
    /// Absolute path of the database dump client — the REAL binary on each
    /// family, never the compatibility symlink beside it.
    dump_binary: String,
    /// Absolute path of the database client a restore drops with and loads
    /// through. A different binary from the dump client: one writes SQL out and
    /// the other reads SQL in.
    client_binary: String,
}

impl ProcessBackupHost {
    /// Creates the host, taking every program path from `distro`.
    #[must_use]
    pub fn new(distro: &dyn DistroAdapter) -> Self {
        Self {
            tar_binary: distro.tar_binary().to_owned(),
            gzip_binary: distro.gzip_binary().to_owned(),
            dump_binary: distro.database_dump_binary().to_owned(),
            client_binary: distro.mysql_client_binary().to_owned(),
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

    /// Spawns the dump client with `dump_arguments` and measures what it
    /// wrote.
    ///
    /// No shell is involved, at any point (rules/security.md item 3): the
    /// arguments reach `execve` one by one, so there is no command line for
    /// anything to re-parse, and the dump reaches its file through the client's
    /// own `--result-file` rather than through a redirect.
    ///
    /// The client's own output is captured and DISCARDED. It is diagnostic
    /// only, and it is the one place a customer's rows can appear in text: a
    /// client refusing halfway prints the statement it choked on. Nothing in
    /// this area has a field to put it in ([`BackupError`]'s shape is the
    /// argument), so it is dropped here rather than carried one frame further.
    ///
    /// # Errors
    ///
    /// [`BackupError::DumpFailed`] for a client that could not be started
    /// (status `PROGRAM_UNAVAILABLE`), one that exited non-zero, and one
    /// whose result file cannot be measured afterwards.
    fn dump_database(&self, database: &DatabaseName, into: &Path) -> Result<u64, BackupError> {
        let arguments = dump_arguments(database, into);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();

        let outcome =
            spawn_argv(&self.dump_binary, &borrowed).map_err(|_| BackupError::DumpFailed {
                status: PROGRAM_UNAVAILABLE,
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
    /// # Errors
    ///
    /// [`BackupError::ArchiveFailed`], as [`Self::dump_database`] reports its
    /// own three cases.
    fn create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError> {
        let arguments = spec.arguments(&self.gzip_binary);
        let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();

        let outcome =
            spawn_argv(&self.tar_binary, &borrowed).map_err(|_| BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            })?;
        if outcome.status != 0 {
            return Err(BackupError::ArchiveFailed {
                status: outcome.status,
            });
        }

        spec.artifact
            .symlink_metadata()
            .map(|metadata| metadata.len())
            .map_err(|_| BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            })
    }

    /// Lists `artifact`'s members with `tar --list`, extracting nothing.
    ///
    /// The names come back exactly as the archive stores them. Nothing is
    /// trimmed, unescaped or normalised here: this output is the pre-scan's
    /// evidence, and a host that tidied a `..` away would be deciding the
    /// question the scan exists to ask.
    ///
    /// # Errors
    ///
    /// [`BackupError::ArchiveFailed`], including for an artifact that is not a
    /// readable gzip stream at all.
    fn list_members(&self, artifact: &Path) -> Result<Vec<String>, BackupError> {
        let compressor = format!("{COMPRESS_PROGRAM}{}", self.gzip_binary);
        let arguments = [LIST, &compressor, FILE, &artifact.to_string_lossy()];
        let borrowed: Vec<&str> = arguments.iter().map(AsRef::as_ref).collect();

        let outcome =
            spawn_argv(&self.tar_binary, &borrowed).map_err(|_| BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            })?;
        if outcome.status != 0 {
            return Err(BackupError::ArchiveFailed {
                status: outcome.status,
            });
        }

        Ok(outcome
            .stdout
            .lines()
            .filter(|line| !line.is_empty())
            .map(str::to_owned)
            .collect())
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
    /// # Errors
    ///
    /// [`BackupError::ArchiveFailed`] when the artifact cannot be OPENED
    /// (`PROGRAM_UNAVAILABLE`), when the archiver refuses or cannot be run,
    /// and — for the account's half — when the archiver refused inside the
    /// child (`ARCHIVER_REFUSED_UNDER_ACCOUNT`, because the child's exit
    /// status does not survive `fork_as_account`).
    ///
    /// [`BackupError::ExtractionIdentityUnavailable`] ONLY when the account
    /// really could not be entered: it could not be resolved, the drop
    /// syscalls failed or did not verify, no child could be forked, or the
    /// child could not be waited for. An archiver that ran as the account and
    /// refused is NOT this variant — that distinction is the whole reason this
    /// method matches on `PrivError` instead of discarding it.
    fn extract(&self, spec: &ExtractSpec) -> Result<(), BackupError> {
        let arguments = spec.arguments(&self.gzip_binary);
        let artifact = File::open(&spec.artifact).map_err(|_| BackupError::ArchiveFailed {
            status: PROGRAM_UNAVAILABLE,
        })?;

        match spec.identity() {
            ExtractIdentity::Root => {
                let borrowed: Vec<&str> = arguments.iter().map(String::as_str).collect();
                run_archiver(&self.tar_binary, &borrowed, Stdio::from(artifact))
            }
            ExtractIdentity::Account(account) => {
                let ids = AccountIds::resolve(&account)
                    .map_err(|_| BackupError::ExtractionIdentityUnavailable)?;
                let program = self.tar_binary.clone();

                // Poison recovery rather than propagation, as `backup_lock`
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
                    run_archiver(&program, &borrowed, Stdio::inherit())
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
    /// # Errors
    ///
    /// [`BackupError::DropFailed`].
    fn drop_database(&self, database: &DatabaseName) -> Result<(), BackupError> {
        let statement = format!("{DROP_STATEMENT} `{}`", database.as_str());
        let borrowed = [CLIENT_BATCH, CLIENT_EXECUTE, statement.as_str()];

        let outcome =
            spawn_argv(&self.client_binary, &borrowed).map_err(|_| BackupError::DropFailed {
                status: PROGRAM_UNAVAILABLE,
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
    /// **The client's own output is never captured: both streams are
    /// `Stdio::null()`, so the kernel discards it and this process never holds
    /// a byte of it.** The doc here used to say it was "captured and
    /// DISCARDED", which is what the sibling [`Self::dump_database`] does —
    /// `spawn_argv` reads the output into a `CommandOutcome` and this area then
    /// drops it — and the wording was borrowed rather than checked.
    ///
    /// The reason there is nothing to capture is the same either way, and it is
    /// why `/dev/null` is the better of the two: this is the one place a
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
    /// opened.
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
        let mut command = Command::new(&self.client_binary);
        apply_child_environment(&mut command);
        let status = command
            .args([CLIENT_BATCH])
            .stdin(Stdio::from(dump))
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .map_err(|_| BackupError::LoadFailed {
                status: PROGRAM_UNAVAILABLE,
            })?;

        if status.code().unwrap_or(PROGRAM_UNAVAILABLE) != 0 {
            return Err(BackupError::LoadFailed {
                status: status.code().unwrap_or(PROGRAM_UNAVAILABLE),
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
/// the parent's threads. It takes the program, the argv and the descriptor the
/// archive arrives on, and nothing else.
///
/// **Not `spawn_argv`, and the reason is that function's own contract:** it
/// gives its child a CLOSED standard input, which is right for every spawn that
/// must not be able to prompt, and fatal for one whose archive arrives there.
/// `spawn_argv` says so in as many words — a caller whose child needs stdin
/// keeps its spawn beside itself, as [`ProcessBackupHost::load_dump`] already
/// does. The environment is still [`apply_child_environment`]'s and nothing
/// else, so no `TAR_OPTIONS` or `LD_PRELOAD` from the unit reaches the
/// archiver, and there is no shell at any point (rules/security.md item 3).
///
/// Both output streams are `Stdio::null()`. There is nothing in
/// [`BackupError`] to carry a diagnostic, and this half of the area reads a
/// CUSTOMER-supplied archive: a `tar` that refuses prints the member name it
/// choked on, which is attacker-chosen text. Not reading it means it is never
/// in this root process's address space.
///
/// # Errors
///
/// [`BackupError::ArchiveFailed`], with [`PROGRAM_UNAVAILABLE`] when the
/// archiver could not be started or was killed by a signal.
fn run_archiver(program: &str, arguments: &[&str], archive: Stdio) -> Result<(), BackupError> {
    let mut command = Command::new(program);
    apply_child_environment(&mut command);
    let status = command
        .args(arguments)
        .stdin(archive)
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .map_err(|_| BackupError::ArchiveFailed {
            status: PROGRAM_UNAVAILABLE,
        })?;

    let code = status.code().unwrap_or(PROGRAM_UNAVAILABLE);
    if code != 0 {
        return Err(BackupError::ArchiveFailed { status: code });
    }

    Ok(())
}
