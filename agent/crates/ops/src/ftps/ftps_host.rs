//! The seam between the FTPS operations and the machine they run on.

use std::path::Path;
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::ftps::ftps_error::FtpsError;
use crate::ftps::model::candidate_outcome::CandidateOutcome;
use crate::safe_write::model::{Reload, Validator};
use crate::sftp::AccountOwnership;
use crate::ssl::CertificateState;

/// The operating-system operations the FTPS area needs.
///
/// A trait rather than direct calls to `std::process::Command`, `std::net` and
/// `std::fs`, and not for abstraction's sake: starting a root daemon, binding a
/// listening socket and connecting to port 21 are exactly the operations a unit
/// test must never really perform. Behind this seam every decision — which
/// error kind is evidence about address families, what a greeting has to begin
/// with, whether a candidate that outlived its deadline is a good one — stays in
/// the operations, where a test can drive it; the one implementation that really
/// touches the machine stays small enough to read in full.
///
/// Implementations MUST spawn with an argv array against an absolute path taken
/// from the `DistroAdapter`, never through a shell and never through a program
/// name resolved by `PATH` (rules/security.md item 3).
pub trait FtpsHost: Send + Sync {
    /// Runs `program` with `arguments` as an argv array and waits for it.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when the program
    /// cannot be started at all. A non-zero exit is NOT an error here — it comes
    /// back in the outcome, because each caller reads a status differently:
    /// `is-active` answers a question with its status, while a refused `restart`
    /// is a failure.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, FtpsError>;

    /// Reads the live configuration file, or answers `None` when it is not there.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ConfigUnreadable`] when the file exists and cannot be
    /// read. Not existing is an answer, not an error — but an unreadable file is
    /// never flattened into "absent", because that would let an I/O failure
    /// authorise a rewrite of a live daemon's configuration.
    fn read_config(&self, target: &Path) -> Result<Option<String>, FtpsError>;

    /// Writes `contents` to `target` through the config-write protocol:
    /// temporary file beside the target, `fsync`, atomic rename, `validator`,
    /// `reload`, and a restoration of the previous content if either refuses
    /// (rules/rust.md "Config writes"). The one implementation delegates to
    /// `crate::safe_write::write_config` and adds nothing of its own.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ServiceRefused`] when the protocol's validator or
    /// reload step exited non-zero — for this file both of those steps are the
    /// service manager, so a refusal there is the daemon or systemd refusing.
    /// Returns [`FtpsError::SpawnFailed`] when one of them could not be started,
    /// and [`FtpsError::ConfigWrite`] for the protocol's own mechanical failures.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), FtpsError>;

    /// Runs `program` against a candidate configuration held in `contents`,
    /// waiting up to `deadline` for it to exit, and killing it if it does not.
    ///
    /// The implementation writes `contents` to a private temporary file that
    /// only root can read, and spawns `program` with THAT path as its first
    /// argument followed by `arguments`. The path is the implementation's own
    /// and never a caller's, so no value from a request reaches an argument
    /// vector here.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] when the candidate cannot be staged or
    /// the program cannot be started. A daemon that started and then exited is
    /// not an error — it is [`CandidateOutcome::Exited`], which is the check's
    /// whole answer.
    fn run_candidate(
        &self,
        program: &str,
        contents: &str,
        arguments: &[&str],
        deadline: Duration,
    ) -> Result<CandidateOutcome, FtpsError>;

    /// Asks the kernel for a TCP port nothing is listening on, by binding
    /// `127.0.0.1:0` and dropping the socket.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when even a
    /// loopback bind fails — a host that cannot do that cannot run the check the
    /// port is for.
    fn ephemeral_port(&self) -> Result<u16, FtpsError>;

    /// Binds an IPv6 TCP listening socket at `[::]:0` the way the daemon would,
    /// and drops it.
    ///
    /// The raw `io::Error` is handed back rather than a typed one on purpose:
    /// the whole point of this call is its errno, and
    /// [`probe_listen_mode`](crate::ftps::probe_listen_mode) is what decides
    /// which kinds are evidence about address families. A seam that mapped the
    /// error would be making that decision where no test can reach it.
    ///
    /// # Errors
    ///
    /// Returns whatever the operating system said, unchanged.
    fn bind_ipv6_listener(&self) -> std::io::Result<()>;

    /// Connects to `port` on the loopback address and reads whatever greeting
    /// arrives, within a bounded timeout.
    ///
    /// Answers `None` when the connection is refused, times out, or produces no
    /// line — every one of which is the same fact for the caller: nothing is
    /// serving. What the greeting has to SAY is the operation's decision, not
    /// this seam's.
    fn control_port_greeting(&self, port: u16) -> Option<String>;

    /// Answers what certificate material is installed for `domain`.
    ///
    /// Delegates to `ops::ssl::certificate_state`, which reads three file names
    /// and parses nothing. It reaches this area through the seam rather than as
    /// a second `&dyn SslHost` parameter on every operation, so an FTPS caller
    /// holds one host and the certificate store keeps exactly one reader.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::ConfigUnreadable`] when a file of the material
    /// exists and cannot be read — the same refusal to turn an I/O failure into
    /// a confident answer that [`Self::read_config`] makes.
    fn certificate_state(&self, domain: &Domain) -> Result<CertificateState, FtpsError>;

    /// Runs `program` with `arguments` as an argv array, writing `stdin` to its
    /// standard input, and waits for it.
    ///
    /// A separate method from [`Self::run`] rather than an `Option<&str>`
    /// parameter on it, and the split is deliberate. There is exactly ONE
    /// caller — `chpasswd`, which takes its `user:password` line on standard
    /// input — while every other spawn in this area is the service manager or
    /// the daemon, none of which reads a byte from its input. Folding the two
    /// would put a `None` at nine call sites to serve one, and would make the
    /// pipe that carries a password look like an ordinary parameter of an
    /// ordinary spawn instead of the one place in the area a secret moves.
    ///
    /// A command line is world-readable through `/proc` on every host this
    /// panel supports, so a password passed as an argument is a password every
    /// local user — including every other tenant's transfer login and every
    /// php-fpm pool — has already read. A pipe is readable only by the two
    /// processes at its ends.
    ///
    /// Implementations must close the pipe after writing, or a tool that reads
    /// to end of input never returns and takes a root daemon's task with it.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::SpawnFailed`] with a negative code when the program
    /// cannot be started, its standard input cannot be taken or written, or it
    /// cannot be waited for. A non-zero exit is NOT an error here — it comes
    /// back in the outcome for the caller to classify.
    fn run_with_stdin(
        &self,
        program: &str,
        arguments: &[&str],
        stdin: &str,
    ) -> Result<CommandOutcome, FtpsError>;

    /// Looks up the numeric identity of the hosting account `account`.
    ///
    /// Behind the seam for the reason every other method here is: a unit test
    /// cannot put an account in the host's password database, and one that
    /// resolved a real name would pass or fail on whichever accounts the
    /// machine running it happens to have.
    ///
    /// The answer is what the FTPS login is created with. It is
    /// [`AccountOwnership`], the type the SFTP area already carries, and not a
    /// second pair of the same two numbers: the argument for giving a transfer
    /// login its account's identity is written out once on that type and is
    /// identical for both daemons, and a second type would be two places for
    /// one decision to drift.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::AccountMissing`] when the password database holds
    /// no such account, or cannot be read.
    fn account_ownership(&self, account: &AccountName) -> Result<AccountOwnership, FtpsError>;

    /// Creates `path`, owned by root, and applies `mode` **to `path` alone**.
    ///
    /// Missing parents are created too, because they must be for the leaf to
    /// exist — but they get the process umask's default and **not** `mode`, and
    /// that is stated rather than implied. This doc used to say "and every
    /// missing parent, at `mode`", which no implementation has ever done: a
    /// reader who believed it would conclude that a missing jail BASE came out
    /// at the mode the caller asked for, when `create_dir_all` gives it 0755.
    /// The base is specified `root:root 0711` — traversable by everyone,
    /// listable by nobody but root, so no account learns the name of another
    /// account's jail from it (`AgentPaths::FTPS_JAIL_ROOT`) — so the
    /// difference is a silently lost security property and not a detail.
    ///
    /// A caller that needs a parent at a mode of its own therefore creates that
    /// parent itself, in its own call, before the leaf.
    /// `ensure_account_jail` does exactly that for the base.
    ///
    /// Idempotent: a directory that is already there is success, because the
    /// jail is ensured on every creation and an account's second FTPS login
    /// must not fail on the first one's work. **A directory that is already
    /// there has its mode re-applied**, so a base whose mode drifted is brought
    /// back rather than left.
    ///
    /// `mode` is applied explicitly rather than left to the process umask.
    /// vsftpd refuses to serve a login whose chroot root the login can write
    /// to, so the mode is not decoration: it is the difference between a
    /// working login and `500 OOPS: vsftpd: refusing to run with writable root
    /// inside chroot()`.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::JailFailed`] when the directory cannot be created
    /// or its mode cannot be set.
    fn create_directory(&self, path: &Path, mode: u32) -> Result<(), FtpsError>;

    /// Lists every FTPS login on this host that belongs to `account`.
    ///
    /// `passwd_database` is the path the `DistroAdapter` gives for the host's
    /// local password database, passed in rather than known here because `ops`
    /// names no platform location of its own.
    ///
    /// An implementation decodes each name the way
    /// [`FtpsUserName::for_account`] built it — at the LAST separator, matching
    /// the WHOLE account — so a name this method reports is a name this agent
    /// could itself have created. A prefix scan would be the wrong predicate:
    /// `alice_` is a prefix of `alice_bob_deploy`, which is account
    /// `alice_bob`'s login.
    ///
    /// **The name alone is not enough**, and `jail_directory` is what settles
    /// it. Account names may contain the separator, so the ACCOUNT `alice_bob`
    /// and the login `bob` of account `alice` are the same eleven characters;
    /// no decode of a name can tell them apart, and one that tried would delete
    /// a neighbouring account's system user as a side effect of removing this
    /// account's logins. Every FTPS login this agent creates has its passwd
    /// home set to its account's FTPS jail, so a candidate belongs to this
    /// account only when its home is exactly that path — a value the agent
    /// itself wrote, not a guess about a name.
    ///
    /// The jail also separates the two DAEMONS. An SFTP login of the same
    /// account has the same uid and a name of the same shape, and its home is
    /// the SFTP jail; the two roots are different directories, so no FTPS
    /// teardown can revoke an SFTP login and no SFTP teardown can revoke this
    /// one.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::AccountMissing`] when the password database cannot
    /// be read at all. An account with no logins is an empty list and not an
    /// error.
    fn account_logins(
        &self,
        passwd_database: &str,
        account: &AccountName,
        jail_directory: &str,
    ) -> Result<Vec<FtpsUserName>, FtpsError>;

    /// Reports whether anything exists at `path`.
    ///
    /// Asked of exactly one thing: whether an account's mount unit was ever
    /// installed. `systemctl disable` refuses a unit it has no file for, so an
    /// account whose FTPS jail was never built must not fail its own deletion
    /// over a unit that was correctly never written.
    ///
    /// It answers `false` for a path that cannot be inspected as well as for
    /// one that is not there, and that is deliberate: the only caller uses the
    /// answer to decide whether to ASK the service manager, and the service
    /// manager's own refusal is what would then be reported.
    fn path_exists(&self, path: &Path) -> bool;

    /// Removes the file at `path`.
    ///
    /// Idempotent: a file that is not there is success, because a deletion
    /// retried after a lost response must converge rather than fail on its own
    /// previous work.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::JailFailed`] when the file is there and cannot be
    /// removed.
    fn remove_file(&self, path: &Path) -> Result<(), FtpsError>;

    /// Removes the directory at `path`, and only if it is empty.
    ///
    /// **Empty-only is the security property of this method, not a
    /// limitation.** The directories it is asked for are an account's jail and
    /// the mount point inside it, and that mount point holds the account's real
    /// home for as long as the bind mount is in place. A recursive removal here
    /// would walk into the mount and delete the customer's entire website; a
    /// removal that refuses a non-empty directory cannot, and its refusal is
    /// exactly the signal that the unmount did not happen.
    ///
    /// Idempotent: a directory that is not there is success, for the same
    /// reason [`Self::remove_file`] is.
    ///
    /// # Errors
    ///
    /// Returns [`FtpsError::JailFailed`] when the directory is there and cannot
    /// be removed — including when it is not empty, which on the mount point
    /// means the mount is still in place.
    fn remove_directory(&self, path: &Path) -> Result<(), FtpsError>;
}
