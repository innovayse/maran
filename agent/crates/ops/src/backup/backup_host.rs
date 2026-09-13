//! The seam between the backup operations and the machine they run on.

use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::account_identity::AccountIdentity;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::extract_spec::ExtractSpec;

/// Everything a backup creation asks of this machine that is not a plain file
/// operation.
///
/// A trait rather than direct calls to `std::process::Command`, and for the
/// reason every area here has one: dumping a real customer's database and
/// reading a real customer's home are precisely the operations a unit test must
/// never actually perform. Behind this seam every decision — which databases,
/// in which order, what a failure costs, what is published and what is deleted
/// — is testable, and the implementations that really touch the machine stay
/// small enough to read in full.
///
/// **One method per SHAPE of spawn, and not the single `execute` the database
/// area's host has.** That area's argument for one method is that a host with a
/// method per operation ends up holding the decisions; it does not carry here,
/// because these spawns do not have one shape to share. Dumping writes a file
/// the caller then hashes and returns a byte count; archiving reads a tree and
/// writes somewhere else; and the clock is not a spawn at all. Collapsing them
/// would mean a caller assembling an argv, which is the thing this area most
/// wants to keep in one reviewed place ([`ArchiveSpec::arguments`]).
///
/// The four methods a RESTORE needs — listing an archive's members, extracting
/// one of them, dropping a database and loading a dump back — landed with the
/// restore operation that uses them, because a trait method whose only
/// implementation has no caller is code nothing has ever run.
///
/// **Every method of this trait MUST be called from
/// `tokio::task::spawn_blocking`, never from a runtime worker.** Each of them
/// waits on a spawned program for as long as a customer's data takes, which on
/// a runtime worker stalls every other in-flight command (rules/rust.md "Async
/// and blocking"). The obligation is restated on each method, because a caller
/// reads the method they are calling.
pub trait BackupHost: Send + Sync {
    /// The current time, in seconds since the Unix epoch.
    ///
    /// Behind the seam and not a `SystemTime::now()` in the operation, for the
    /// reason `CronHost::new_entry_id` is behind its own: an operation that
    /// read the host's clock directly would be untestable, because every
    /// assertion about the manifest it writes would have to be written against
    /// a value that changes on every run (rules/testing.md "Determinism").
    fn now_unix(&self) -> i64;

    /// Resolves `account`'s numeric identity out of the host's password
    /// database, as of NOW.
    ///
    /// Behind the seam and not a direct `AccountIds::resolve` in the operation,
    /// for the reason `now_unix` is behind it: an operation that read the real
    /// password database could only be tested on a machine that happened to
    /// have the account, and the thing worth testing here is what the operation
    /// does when the answer CHANGES between two calls.
    ///
    /// It is asked twice per restore on purpose — once to decide who the
    /// staging tree is filled as, and once immediately before the home swap, to
    /// decide whether that first answer is still true. A uid read at the start
    /// of an operation that runs for hours is a check-then-act window an hour
    /// wide, and the value at the end of it is what `chown` would act on.
    ///
    /// Unlike the other methods here this one spawns nothing, so it carries no
    /// `spawn_blocking` obligation of its own; it is still called from inside
    /// one, because its callers are.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::ExtractionIdentityUnavailable`] when the password
    /// database holds no such account or cannot be read — which, asked the
    /// second time, is the account having been removed while the restore ran.
    fn account_identity(&self, account: &AccountName) -> Result<AccountIdentity, BackupError>;

    /// Dumps `database` into the file at `into` and answers its size in bytes.
    ///
    /// `into` is always inside the root-only scratch. A dump must never be
    /// written anywhere an account can reach: it holds every row of a
    /// customer's database, and a file a customer can replace between the dump
    /// and the archive is a file that reaches a restore's loader — which
    /// connects as the database superuser.
    ///
    /// Implementations MUST write the file through the client's own
    /// `--result-file` and never through a shell redirect (rules/security.md
    /// item 3): there is no shell here, so there is nothing to redirect with.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::DumpFailed`] when the client refuses or cannot be
    /// run, and when the file it was told to write cannot be measured
    /// afterwards — a dump whose size is unknown is a dump nothing downstream
    /// can check.
    fn dump_database(&self, database: &DatabaseName, into: &Path) -> Result<u64, BackupError>;

    /// Runs the archiver over `spec` and answers the artifact's size in bytes.
    ///
    /// Implementations MUST build the argv from
    /// [`ArchiveSpec::arguments`] rather than assembling one, so that the flags
    /// this product's safety depends on — `--one-file-system`, the absence of
    /// `--dereference` — are stated in one reviewed place.
    ///
    /// The compressor that builder is handed MUST be the `DistroAdapter`'s
    /// `gzip_binary()` and MUST be absolute. An implementation that passed a
    /// bare name, or that reverted to `--gzip`, would put a `PATH` lookup back
    /// inside a child of a root daemon — which is what this argument exists to
    /// keep out.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::ArchiveFailed`] when the archiver refuses or
    /// cannot be run, and when the artifact cannot be measured afterwards.
    fn create_archive(&self, spec: &ArchiveSpec) -> Result<u64, BackupError>;

    /// Lists every member name in `artifact`, writing nothing anywhere.
    ///
    /// This is the read a restore's pre-scan is built on, and it is a separate
    /// method precisely so that "what is in this archive" can be answered
    /// before any question of where its contents would go arises. An
    /// implementation MUST NOT extract, and MUST NOT create the destination of
    /// anything it reads: the caller has not yet decided that this archive may
    /// be unpacked at all.
    ///
    /// The names come back exactly as the archive stores them, including any
    /// `..` or leading `/` a hostile archive put there. Sanitising here would
    /// destroy the very evidence the pre-scan exists to look at.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::ArchiveFailed`] when the archiver refuses or
    /// cannot be run — which for this method includes an artifact that is not a
    /// readable gzip stream at all.
    ///
    /// An implementation that decompresses through a named program MUST name it
    /// absolutely, exactly as [`Self::create_archive`] must: this method reads
    /// a customer-supplied artifact, so it is the last place the decompressor
    /// should be chosen by an environment variable.
    fn list_members(&self, artifact: &Path) -> Result<Vec<String>, BackupError>;

    /// Extracts one member of an archive, as the identity `spec` derives,
    /// **reading the archive from a descriptor the implementation opens as root
    /// and never from a path the archiver is given.**
    ///
    /// Implementations MUST build the argv from [`ExtractSpec::arguments`],
    /// handing it the `DistroAdapter`'s absolute `gzip_binary()` for the reason
    /// [`Self::create_archive`] gives, and MUST run as
    /// [`ExtractSpec::identity`] says — root for the manifest and
    /// the dumps, and inside `fork_as_account` for the home (R3/R4). The two
    /// are derived from one field of the spec so that they cannot be paired the
    /// wrong way round; an implementation that reads the member and chooses its
    /// own identity has undone that.
    ///
    /// # The artifact arrives as an open file, and this is the contract
    ///
    /// That argv names `--file -`. [`ExtractSpec::artifact`] is therefore NOT
    /// an argument to the archiver: it is the path the IMPLEMENTATION opens,
    /// as root, before any privilege is dropped, and the open file becomes the
    /// archiver's standard input.
    ///
    /// An implementation MUST NOT name the artifact on the archiver's command
    /// line, and MUST NOT relax the artifact's ownership or mode to make a path
    /// work. Two of this feature's guarantees meet here — the home is extracted
    /// AS THE ACCOUNT (R3), and a published artifact is `0600` inside a `0700`
    /// root-owned directory — and a path argument satisfies neither for the
    /// price of the other: the customer's archiver cannot open that file, and
    /// the only way to let it would be to publish the customer's backup to
    /// every account on the host. A descriptor satisfies both, because opening
    /// is root's act and reading is the child's.
    ///
    /// An implementation that forks MUST also account for the fork:
    /// `fork_as_account`'s child closes every descriptor above 2 before it runs
    /// any work, so the artifact has to be ON standard input by the time of the
    /// fork rather than handed over afterwards.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::ArchiveFailed`] when the artifact cannot be
    /// opened, and when the archiver refuses or cannot be run — INCLUDING when
    /// it refuses inside the unprivileged child.
    ///
    /// Returns [`BackupError::ExtractionIdentityUnavailable`] only when the
    /// account the home must be extracted as could not be ENTERED: not
    /// resolved, not dropped to, not verified, not forked, not waited for. An
    /// archiver that ran as the account and refused is the first variant and
    /// not this one. The two are separate answers because they send an operator
    /// to different places — a user database and an archive — and an
    /// implementation that reports one for the other is a defect of the same
    /// severity as a wrong extraction (rules/architecture.md).
    fn extract(&self, spec: &ExtractSpec) -> Result<(), BackupError>;

    /// Drops `database`, if it is there.
    ///
    /// **This is the point of no return of a restore**, and an implementation
    /// carries none of the judgement about when it may be called — the
    /// operation takes that database's rollback dump on the line before this
    /// one.
    ///
    /// A database that is already gone is not a failure: repeating an operation
    /// converges, and a restore retried after a crash between the drop and the
    /// load must be able to continue rather than refuse forever.
    ///
    /// There is no matching `create_database`. The archive's dump was taken
    /// with `--databases`, so it carries its own `CREATE DATABASE` and `USE` —
    /// the reason that flag is on the create side's argv — and a `CREATE` here
    /// would be this agent composing DDL of its own beside DDL it already has.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::DropFailed`] when the client refuses or cannot be
    /// run.
    fn drop_database(&self, database: &DatabaseName) -> Result<(), BackupError>;

    /// Loads the SQL at `from` into the server, re-creating `database`.
    ///
    /// `from` is ALWAYS a file inside the root-only scratch, and that is a
    /// security property rather than a convention (R4): this client connects as
    /// the database superuser, so a dump file an account could replace between
    /// its extraction and this call is arbitrary SQL as that superuser — a
    /// `FILE` privilege away from reading `/etc/shadow` into a table.
    ///
    /// The file reaches the client on stdin, as the account area feeds
    /// `chpasswd`. There is no shell and therefore no redirect
    /// (rules/security.md item 3).
    ///
    /// `database` says WHICH database this dump is for — it is what a caller
    /// names in an error, and what an implementation may check the dump against
    /// — and an implementation MUST NOT pass it to the client as the connection's
    /// default database. This method is called immediately after
    /// [`Self::drop_database`], so at that moment the database does not exist,
    /// and a client told to open it refuses at connect time without reading a
    /// byte of the dump. The dump carries its own `CREATE DATABASE` and `USE`;
    /// that is what re-creates it.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::LoadFailed`] when the client refuses, cannot be
    /// run, or the dump file cannot be read.
    fn load_dump(&self, database: &DatabaseName, from: &Path) -> Result<(), BackupError>;
}
