//! Why a local backup root was refused.

/// Reasons a candidate is not a directory backup artifacts may rest in.
///
/// Every variant below is a refusal from
/// [`super::local_backup_root::LocalBackupRoot::parse`] except the last two,
/// which come from
/// [`super::local_backup_root::LocalBackupRoot::resolve`]. That split is the
/// type's contract in enum form: the first six can be decided by reading the
/// operator's string, the last two only by asking the filesystem, and ownership
/// and mode are not here at all because they belong to the inode at the moment
/// of the write.
///
/// A backup holds the account's files and a dump of its database — whatever the
/// customer's application put there, including other people's personal data. It
/// is the most sensitive object this product creates, which is why the list is
/// this long and why each entry names a way the artifact stops being root's.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum LocalBackupRootError {
    /// The candidate did not begin with `/`.
    ///
    /// The empty string lands here too. A relative root means whatever the
    /// process's working directory means at the moment it is used, and the
    /// agent's working directory is not a thing an operator configured.
    #[error("a backup root is an absolute path")]
    NotAbsolute,

    /// The candidate is the filesystem root, ends in `/`, or holds an empty,
    /// `.` or `..` component.
    ///
    /// Refused rather than normalised, and for a reason that is not tidiness:
    /// the startup check, the pre-write check and the write itself must all be
    /// talking about the same directory, and a directory with several spellings
    /// is one a check can approve under one spelling while a write uses
    /// another. Refusing `..` before the prefix comparisons is also what makes
    /// those comparisons mean anything — `/tmp/../home/acme` is under both of
    /// the prefixes it appears to be under neither of.
    #[error(
        "write a backup root as one canonical absolute path, with no `.`, `..` or trailing `/`"
    )]
    NotCanonical,

    /// The candidate is, or is under, the root holding every account's home.
    ///
    /// The refusal that matters most and reads as the most arbitrary. Under a
    /// home the directory counts against the customer's quota and is swept into
    /// the next backup of that account, recursively — but neither of those is
    /// the defect. The defect is that the directory is WRITABLE BY THE
    /// CUSTOMER, so the artifact a later restore reads is an artifact the
    /// customer chose, and a restore reads it as root. That is a root-side
    /// extraction of attacker-supplied content, which is a different and much
    /// larger problem than a quota.
    #[error(
        "a backup root may not be under the account home root — a customer can replace what a restore reads"
    )]
    UnderAccountHomes,

    /// The candidate is, or is under, a directory a web server serves from.
    ///
    /// `curl https://example.com/backups/<id>.tar.gz` downloads the customer's
    /// database. The list covers both families' packaged document roots on both
    /// families, because being wrong here can only refuse a directory an
    /// operator can move.
    #[error(
        "a backup root may not be under a web server's document root — the artifact would be downloadable"
    )]
    UnderSiteRoot,

    /// The candidate is, or is under, a world-writable directory.
    ///
    /// `/tmp`, `/var/tmp` and `/dev/shm`. Another local user pre-plants the
    /// name root is about to write, and root then writes through it.
    #[error("a backup root may not be under a world-writable directory")]
    WorldWritableAncestor,

    /// The candidate was longer than a path may be.
    #[error("a backup root is at most {maximum} bytes, not {actual}")]
    TooLong {
        /// The longest path the kernel accepts.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// The path resolves somewhere other than itself: a component is a symlink.
    ///
    /// Refused rather than followed. Following it would mean the directory a
    /// root-owned archive is written into is chosen by whoever can rewrite the
    /// link, which is precisely the property a backup root must not have. Only
    /// [`super::local_backup_root::LocalBackupRoot::resolve`] can return this —
    /// no reading of the string can.
    #[error("a component of the backup root is a symlink; give the path it resolves to")]
    SymlinkComponent,

    /// The path could not be canonicalised at all.
    ///
    /// It does not exist yet, or a component is not readable. Reported as
    /// unproven and not as safe: the check could not observe what it reports
    /// on, and the honest answer to "is any component a symlink" is then "I do
    /// not know" (rules/testing.md).
    #[error("the backup root could not be resolved, so nothing about it has been proven")]
    Unresolvable,
}
