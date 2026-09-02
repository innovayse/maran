//! Failures of the SFTP user operations.

/// The `code` reported when a program could not be started at all.
///
/// Negative so it can never collide with an exit status, every one of which is
/// between 0 and 255 — an operator reading `SpawnFailed { code: -1 }` knows the
/// tool never ran, rather than looking up status -1 in its manual.
const PROGRAM_UNAVAILABLE: i32 = -1;

/// `useradd`'s status for a name that is already taken.
///
/// The shadow suite's `E_NAME_IN_USE`. It is what makes creation idempotent
/// without a check-then-create race: `useradd` decides, atomically, whether the
/// name was free, and this module only has to read its answer.
const NAME_IN_USE: i32 = 9;

/// `userdel`'s status for a user that is not there.
///
/// The shadow suite's `E_NOTFOUND`, and the mirror image of [`NAME_IN_USE`]: a
/// repeated deletion converges on [`SftpError::NotFound`] rather than failing.
const NO_SUCH_USER: i32 = 6;

/// What can go wrong while creating, re-crediting or deleting an SFTP user.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"), and a
/// deliberately narrow one: **no variant carries a program's output**. Every
/// payload is an `i32`, so there is no field a message could be put in. The
/// realistic leak here is not a careless log line — it is `chpasswd` or PAM
/// quoting back the line it refused, which contains the customer's password in
/// full. A shape that cannot hold a string cannot hold that
/// (rules/security.md item 8).
///
/// The cost to an operator is accepted and real: a refusal that is not one of
/// the named conditions arrives as [`Self::SpawnFailed`] with the tool's exit
/// status and nothing else. The status is enough to find the condition in the
/// tool's manual, and the panel's own record supplies the rest.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum SftpError {
    /// A system user of that name is already on this host.
    ///
    /// The idempotent answer to a repeated creation. It is deliberately NOT
    /// folded into [`Self::SpawnFailed`]: the panel has to tell "the login is
    /// there" from "the tool said no", and — the reason that matters more —
    /// this answer is returned BEFORE any password is set, so a retry of a
    /// creation that already succeeded cannot reset the credential the customer
    /// was shown.
    #[error("the sftp user already exists")]
    AlreadyExists,

    /// No system user of that name is on this host.
    ///
    /// The idempotent answer to a repeated deletion.
    #[error("the sftp user was not found")]
    NotFound,

    /// A tool refused for a reason this area does not name, or could not be run.
    ///
    /// Carries the exit status and nothing else — see the note on the enum for
    /// why there is no room for the output beside it.
    #[error("an sftp user tool failed with status {code}")]
    SpawnFailed {
        /// The tool's exit status, or `-1` when it could not be started at all.
        code: i32,
    },

    /// `chpasswd` refused the password line.
    ///
    /// Its own variant rather than a [`Self::SpawnFailed`] status, because it is
    /// the one failure here that leaves a usable login the customer cannot use:
    /// the account exists, and the password it was to be reachable with was not
    /// set. The panel has to be able to say that specifically.
    #[error("the password was refused")]
    PasswordRejected,

    /// The hosting account the login was to belong to is not on this host.
    ///
    /// Its own variant rather than a [`Self::NotFound`], which is about the
    /// LOGIN: told apart because the two send an operator to different places.
    /// A missing login is the idempotent answer to a repeated deletion; a
    /// missing account means the panel asked for a file-transfer credential
    /// against an account this host never created, and the login must not be
    /// created at all — one made without the account's identity could read
    /// nothing the account owns.
    #[error("the hosting account does not exist on this host")]
    AccountMissing,

    /// The account's jail could not be brought to the state SFTP needs.
    ///
    /// The jail directory, its mount point, or the bind-mount unit that fills
    /// it. Its own variant because the login is worthless without it: a user
    /// created against a jail that is not mounted logs in successfully and finds
    /// an empty directory where its files should be, which reads to a customer
    /// as data loss.
    #[error("the sftp jail could not be prepared")]
    JailFailed,
}

impl SftpError {
    /// Classifies a `useradd` that exited non-zero.
    ///
    /// The status is read rather than the output, so no message the tool printed
    /// can reach a caller through this type.
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

    /// The error for a tool that could not be started at all.
    pub(crate) fn program_unavailable() -> Self {
        Self::SpawnFailed {
            code: PROGRAM_UNAVAILABLE,
        }
    }
}
