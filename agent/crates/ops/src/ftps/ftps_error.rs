//! Failures of the FTPS daemon operations.

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;

use crate::safe_write::SafeWriteError;

/// The `code` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `SpawnFailed { code: -1 }` knows the
/// tool never ran, rather than looking up status -1 in its manual.
pub(crate) const PROGRAM_UNAVAILABLE: i32 = -1;

/// What can go wrong while enabling, disabling, reloading or reading the FTPS
/// daemon, and while creating an FTPS login, setting its password or removing it.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"). Three of the
/// variants carry text, and all are deliberate: [`Self::Bound`] names the path
/// of the program that was stopped and the bound, never a byte it printed,
/// [`Self::CertificateMissing`] names the path an operator has to put material
/// at, and [`Self::ConfigRejected`] carries what the daemon printed when it
/// refused a candidate configuration. None of them can hold a credential — the
/// login half of the area reports a refused password as
/// [`Self::PasswordRejected`], which carries nothing at all — and all
/// are operator-facing text that the panel maps to a localized message rather
/// than showing verbatim to a hosting customer (rules/rust.md "Errors").
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum FtpsError {
    /// No certificate material is installed for the hostname the daemon was
    /// asked to serve.
    ///
    /// FTPS never creates certificate material: the certificate store is
    /// `ops::ssl`'s, and a daemon that generated its own placeholder would put
    /// a certificate a customer's client warns about in front of the panel's
    /// own promise of forced TLS. So the refusal is the whole behaviour, and it
    /// names `expected_path` because a refusal that cannot say WHERE the
    /// material has to be is a refusal an operator cannot act on.
    #[error("no certificate material for {domain} at {expected_path}")]
    CertificateMissing {
        /// The hostname the daemon would have served.
        domain: String,
        /// Absolute path the certificate chain has to be installed at.
        expected_path: String,
    },

    /// The daemon refused the candidate configuration before it was swapped in.
    ///
    /// `output` is what the daemon printed, and it is frequently EMPTY: measured
    /// on both families on 2026-09-08, the Debian family's build exits 2 saying
    /// nothing at all for every refusal — a bad boolean, an unknown key, an
    /// unloadable certificate, a bound port. `output_is_unavailable_on_this_platform`
    /// records that, so the panel's message can say "vsftpd refused the
    /// configuration and this platform's build prints no reason" instead of
    /// pretending to know one.
    #[error("vsftpd refused the candidate configuration: {output}")]
    ConfigRejected {
        /// Everything the daemon printed while refusing, possibly empty.
        output: String,
        /// Whether `output` is empty because this platform's build says nothing,
        /// rather than because nothing went wrong.
        output_is_unavailable_on_this_platform: bool,
    },

    /// The service manager would not bring the unit up on the configuration.
    ///
    /// Raised when the restart itself fails, and equally when the restart
    /// succeeds and the unit is not active afterwards — a `Type=simple` unit's
    /// start returns as soon as the process has been forked, so those are two
    /// questions and not one.
    #[error("the service manager refused {unit}")]
    ServiceRefused {
        /// Name of the systemd unit that would not come up.
        unit: String,
    },

    /// The unit is active and nothing answers on the control port.
    ///
    /// The failure a configuration parse cannot see: a certificate whose key
    /// does not match parses perfectly and dies at the first handshake, and a
    /// port something else already holds leaves a unit systemd is happy with.
    /// This is what the post-swap observation exists to catch, and reaching it
    /// restores the previous configuration. It is only returned after the bounded
    /// wait for the control port (`wait_for_control_port`) has run out, so a
    /// daemon that is merely slow to bind is not reported as silent.
    #[error("the ftps daemon is not answering on its control port")]
    NotListening,

    /// A tool refused for a reason this area does not name, or could not be run.
    ///
    /// Carries the exit status alone. A negative status means the program never
    /// ran at all — see `Self::program_unavailable`.
    #[error("an ftps tool failed with status {code}")]
    SpawnFailed {
        /// The program's exit status, or a negative sentinel when it never ran.
        code: i32,
    },

    /// A program of this area outran its deadline or its output ceiling, and was
    /// killed together with its process group.
    ///
    /// Its own variant and never a verdict: a tool that was killed has not said
    /// that the daemon, the login or the configuration is good or bad, and it may
    /// have finished on the host after the agent stopped waiting. So this is never
    /// folded into [`Self::ServiceRefused`], [`Self::ConfigRejected`] or
    /// [`Self::SpawnFailed`], each of which states something the agent knows.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

    /// The daemon configuration could not be rendered.
    ///
    /// Only reachable if the template and its render type have drifted apart,
    /// which a golden test catches long before a host does.
    #[error("the ftps configuration could not be rendered")]
    Render,

    /// The configuration file could not be read.
    ///
    /// A separate condition from every other one here, and never flattened into
    /// "there is no configuration": a file that exists and cannot be read is the
    /// state in which the agent does not know what the daemon is serving, and an
    /// operation that answered "absent" there would rewrite a live file on the
    /// strength of an I/O failure.
    #[error("the ftps configuration could not be read")]
    ConfigUnreadable,

    /// The configuration file could not be written through the config-write
    /// protocol.
    ///
    /// The protocol's own mechanical failures — a temporary file that cannot be
    /// created, an `fsync` that fails, a rename that fails, a rollback that
    /// fails — as distinct from the daemon refusing what was written, which is
    /// [`Self::ServiceRefused`] or [`Self::NotListening`]. `reason` is the
    /// protocol's typed error rendered for the operator log.
    #[error("the ftps configuration could not be written: {reason}")]
    ConfigWrite {
        /// What the config-write protocol said, for the operator log.
        reason: String,
    },

    /// The login is already on this host, and its password was NOT changed.
    ///
    /// The idempotent answer to a creation whose response was lost: the caller
    /// cannot tell a lost request from a lost reply, and a second attempt that
    /// reset the credential would invalidate the password the customer has
    /// already been shown. `useradd`'s own exit status decides, so there is no
    /// check-then-create gap for a second creation to slip through.
    #[error("the ftps login already exists")]
    AlreadyExists,

    /// The host holds no FTPS login of that name.
    ///
    /// The idempotent answer to a repeated deletion, and the reason the
    /// account-deletion cascade can retry after a lost response: a login that
    /// went away between the enumeration and its removal is the state the
    /// caller wanted anyway.
    #[error("no such ftps login")]
    NotFound,

    /// The account's jail could not be built, mounted, or taken away.
    ///
    /// The sharpest instance is the teardown's: the jail directories are
    /// removed with `remove_dir`, which refuses a directory that is not empty,
    /// and a mount point that is still mounted is exactly a non-empty
    /// directory. So this variant is what a bind mount that did not come down
    /// looks like — and the refusal is the safety property, because under that
    /// mount point is the customer's real home.
    #[error("the ftps jail could not be built or removed")]
    JailFailed,

    /// The hosting account is not on this host.
    ///
    /// Checked before anything is created, because a jail for an account that
    /// does not exist is a root-owned directory nothing will ever mount into,
    /// and a login carrying an unresolvable uid is a credential with no owner.
    #[error("the hosting account does not exist")]
    AccountMissing,

    /// `chpasswd` refused the `user:password` line.
    ///
    /// Its own variant rather than a [`Self::SpawnFailed`], because it means
    /// something an operator acts on differently: the login EXISTS and its
    /// password is unchanged, where a refused `useradd` means there is no login
    /// at all.
    #[error("the ftps login's password was refused")]
    PasswordRejected,

    /// Another operation for the hosting account is already running on this
    /// host.
    ///
    /// The account's lock (`crate::accounts::account_lock`) refusing rather
    /// than waiting — no new lock is introduced by this area (rules/rust.md,
    /// "What this agent serialises"). Nothing was created and nothing was
    /// written.
    #[error("another operation for this account is already running")]
    AccountBusy,

    /// The hosting account's uid or gid moved, or the account went away,
    /// between the first read and `useradd`.
    ///
    /// `useradd --non-unique --uid` accepts a number that now belongs to
    /// somebody else; it is the one tool in this path that cannot be trusted to
    /// notice. No login is created in that case.
    #[error("the hosting account's identity changed while the login was being created")]
    AccountIdentityChanged,

    /// `getent` printed something this agent cannot read as the shadow entry of
    /// the login it asked about.
    ///
    /// Its own variant rather than an [`Self::AccountMissing`] or a silent
    /// default, because the caller that reads a shadow field decides whether to
    /// re-lock on the answer: an unreadable field read as "no password" would
    /// make an ordinary password change LOCK a working login, and read as "has a
    /// password" would hand a suspended customer their login back. Neither is
    /// acceptable, so the read fails instead.
    #[error("the ftps login's password state could not be read")]
    StatusUnreadable,

    /// The password was set, but the login can authenticate although it could
    /// not before.
    ///
    /// The one condition in this area an operator has to act on: the password IS
    /// set and the login is OPEN. `usermod --lock` exits zero on a login it did
    /// nothing to, so this is raised from a re-read of the raw shadow field and
    /// never from an exit status.
    #[error("the ftps login's suspension could not be restored after the password was set")]
    SuspensionNotRestored,

    /// The name is not a hosting account this panel may act on: it is reserved,
    /// has a system identity, or does not have the managed home.
    ///
    /// Raised before any jail, login or password is touched. A name with no row
    /// in the local password database at all is [`Self::AccountMissing`]
    /// instead. Deliberately one variant for the three refusals, and without the
    /// reason: naming which check failed would describe the rules to whoever is
    /// probing them. The reason is carried only by the wrapped value of the
    /// type that decided; this variant drops it.
    #[error("the account is not a hosting account")]
    AccountNotManaged,
}

/// `useradd`'s status for a name that is already taken.
///
/// The shadow suite's `E_NAME_IN_USE`. It is what makes creation idempotent
/// without a check-then-create race: `useradd` decides, atomically, whether the
/// name was free, and this area only has to read its answer.
const NAME_IN_USE: i32 = 9;

/// `userdel`'s status for a user that is not there.
///
/// The shadow suite's `E_NOTFOUND`, and the mirror image of [`NAME_IN_USE`]: a
/// repeated deletion converges on [`FtpsError::NotFound`] rather than failing.
const NO_SUCH_USER: i32 = 6;

impl From<SpawnError> for FtpsError {
    /// Maps a failed spawn onto this area's list.
    ///
    /// A bound becomes [`Self::Bound`]. Every other failure means the program
    /// never produced an answer, so it becomes [`Self::program_unavailable`],
    /// which is what the area's tests and callers already read as "could not
    /// run it". `SpawnError` is `#[non_exhaustive]`, so a variant added later
    /// lands on the same answer.
    ///
    /// Each area keeps its own copy of this small mapping on purpose: the
    /// non-bound target differs per area (`SafeWriteError::from_spawn`,
    /// `AccountError`, `SslOpError` and the rest each name their own), so the
    /// arms are not interchangeable and are not to be folded into one helper.
    fn from(error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            _ => Self::program_unavailable(),
        }
    }
}

impl FtpsError {
    /// Maps a failure of the config-write protocol onto this area's list.
    ///
    /// A validator or reload that exited non-zero is [`Self::ServiceRefused`],
    /// since for the daemon's file both steps are the service manager acting on
    /// the daemon. A program that could not be started is
    /// [`Self::program_unavailable`]. A bound stays [`Self::Bound`]: a tool that
    /// was killed at its deadline said nothing about the configuration, so it is
    /// not a refusal and not a write failure. Every other failure is
    /// [`Self::ConfigWrite`] carrying the protocol's message for the operator
    /// log.
    #[must_use]
    pub(crate) fn from_config_write(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { .. } | SafeWriteError::ReloadFailed { .. } => {
                Self::ServiceRefused {
                    unit: AgentPaths::FTPS_UNIT.to_owned(),
                }
            }
            SafeWriteError::SpawnFailed { .. } => Self::program_unavailable(),
            SafeWriteError::Bound(bound) => Self::Bound(bound),
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }

    /// The error for a program that could not be started at all.
    #[must_use]
    pub fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }

    /// Classifies a `useradd` that exited non-zero.
    ///
    /// The status is read rather than the output, so no message the tool
    /// printed can reach a caller through this type.
    pub(crate) fn from_useradd(status: i32) -> Self {
        if status == NAME_IN_USE {
            return Self::AlreadyExists;
        }

        Self::SpawnFailed { code: status }
    }

    /// Classifies a `userdel` that exited non-zero.
    pub(crate) fn from_userdel(status: i32) -> Self {
        if status == NO_SUCH_USER {
            return Self::NotFound;
        }

        Self::SpawnFailed { code: status }
    }

    /// Maps the refusal of a managed-account resolution onto this area's list.
    ///
    /// An absent row is [`Self::AccountMissing`], the answer the area always
    /// gave for an account the host does not hold; every other refusal
    /// (reserved, system identity, wrong home) is [`Self::AccountNotManaged`].
    /// `ManagedAccountError` is `#[non_exhaustive]`, so a refusal added later
    /// lands on the stricter answer.
    pub(crate) fn from_managed_account(error: ManagedAccountError) -> Self {
        match error {
            ManagedAccountError::NotFound => Self::AccountMissing,
            _ => Self::AccountNotManaged,
        }
    }
}

#[cfg(test)]
#[path = "../tests/ftps/ftps_error_tests.rs"]
mod tests;
