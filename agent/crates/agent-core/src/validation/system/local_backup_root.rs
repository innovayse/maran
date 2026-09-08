//! The directory the operator's local backup artifacts rest in.

use std::path::{Path, PathBuf};

use crate::agent_paths::AgentPaths;

use super::local_backup_root_error::LocalBackupRootError;

/// The root the panel ships with, and the value an operator who configures
/// nothing gets.
///
/// Written as the constant `agent_paths` already publishes rather than as a
/// second literal, so "the directory the agent writes to" and "the directory
/// this type approves" cannot drift into two different strings.
const DEFAULT_LOCAL_BACKUP_ROOT: &str = AgentPaths::BACKUP_ROOT;

/// The longest path this type accepts, in bytes — Linux's `PATH_MAX`.
///
/// A candidate longer than the kernel accepts cannot be the directory anything
/// is written to, so it is refused here rather than surviving as a value whose
/// every use fails with `ENAMETOOLONG`.
const MAXIMUM_LENGTH: usize = 4096;

/// Directory prefixes that are world-writable on every system this product
/// supports.
///
/// Not a platform fact needing the distro adapter: these three are world
/// writable by definition of what they are for, on every family, and the list
/// is a REFUSAL rather than a location the agent uses — so being wrong about a
/// family here can only refuse more, never write somewhere unexpected.
const WORLD_WRITABLE_ROOTS: [&str; 3] = ["/tmp", "/var/tmp", "/dev/shm"];

/// Directory prefixes a web server serves from on the families this product
/// supports.
///
/// Same reasoning as [`WORLD_WRITABLE_ROOTS`]: a refusal list, so both
/// families' defaults are refused on both families. A site created BY this
/// panel lives under the account's home and is refused by
/// [`LocalBackupRootError::UnderAccountHomes`] before this list is reached;
/// this list is for the document roots an operator's own web server may have
/// been serving before the panel arrived.
const SITE_ROOTS: [&str; 4] = [
    "/var/www",
    "/srv/www",
    "/usr/share/nginx",
    "/usr/share/httpd",
];

/// A validated local backup root: an absolute, canonically spelled directory
/// that is not somewhere a backup must never rest.
///
/// The inner path is private and the only constructor is
/// [`LocalBackupRoot::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// **What this type answers is a question about a STRING, and only that.**
/// `parse` reads the operator's text and decides whether the text names a place
/// an archive of a customer's files and database may rest. It does not look at
/// the filesystem, so it says nothing about whether the directory exists, who
/// owns it, or what its mode is. Those are questions about an INODE, they have
/// different answers at different moments, and they are asked separately — at
/// startup and again immediately before every write, by `stat`ing the path the
/// write actually goes to. Reading this type as "the backup root is safe" is
/// the misreading it is documented against: it means "the operator did not
/// name one of the places that are wrong no matter what the inode says".
///
/// [`LocalBackupRoot::resolve`] is the one member that leaves the string world,
/// and it is separate for the same reason: it asks the filesystem whether any
/// component is a symlink, which no amount of reading the text can decide.
///
/// The refusal list is not a collection of tastes; each entry is a way the
/// artifact stops being root's. In descending order of how easy it is to miss:
///
/// - **Never under `/home`.** This is the one worth reading twice. Under a home
///   the directory counts against the customer's quota and lands inside the
///   next backup of that account, recursively — but the defect is that it is
///   *writable by the customer*, so the artifact a later restore reads is an
///   artifact the customer chose, and a restore reads it as ROOT. That turns a
///   backup root into an arbitrary-content channel into a privileged
///   extraction.
/// - **Never under a document root.** `curl https://example.com/backups/<id>.tar.gz`
///   is the whole argument.
/// - **Never `/tmp`, `/var/tmp` or `/dev/shm`.** World-writable, so another
///   local user pre-plants the name root is about to write.
/// - **Absolute and canonically spelled.** A relative path means whatever the
///   process's working directory means at the moment it is used, and a
///   directory with two spellings is a directory a check approves under one and
///   a write uses under the other.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct LocalBackupRoot(PathBuf);

impl LocalBackupRoot {
    /// Validates `candidate` as a local backup root and wraps it.
    ///
    /// A string check, in the order the refusals make each other meaningful:
    /// length, then absoluteness, then canonical spelling — which is what makes
    /// the prefix comparisons below trustworthy, since `/tmp/../home` is
    /// already gone by then — and then the three refusal lists.
    ///
    /// # Errors
    ///
    /// - [`LocalBackupRootError::TooLong`] when `candidate` exceeds `PATH_MAX`.
    /// - [`LocalBackupRootError::NotAbsolute`] when it does not begin with `/`,
    ///   which is also what refuses the empty string.
    /// - [`LocalBackupRootError::NotCanonical`] when it is the filesystem root
    ///   itself, ends in `/`, or holds an empty, `.` or `..` component — every
    ///   one of which is a second spelling of some directory that already has
    ///   one.
    /// - [`LocalBackupRootError::UnderAccountHomes`] when it is, or is under,
    ///   the account home root — where the customer can replace the artifact a
    ///   root-side restore reads.
    /// - [`LocalBackupRootError::UnderSiteRoot`] when it is, or is under, a
    ///   directory a web server serves from.
    /// - [`LocalBackupRootError::WorldWritableAncestor`] when it is, or is
    ///   under, `/tmp`, `/var/tmp` or `/dev/shm`.
    ///
    /// [`LocalBackupRootError::SymlinkComponent`] and
    /// [`LocalBackupRootError::Unresolvable`] are NOT returned here: they are
    /// [`LocalBackupRoot::resolve`]'s answers, because they are the
    /// filesystem's.
    pub fn parse(candidate: &str) -> Result<Self, LocalBackupRootError> {
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(LocalBackupRootError::TooLong {
                maximum: MAXIMUM_LENGTH,
                actual: candidate.len(),
            });
        }

        if !candidate.starts_with('/') {
            return Err(LocalBackupRootError::NotAbsolute);
        }

        if candidate.len() == 1 || candidate.ends_with('/') {
            return Err(LocalBackupRootError::NotCanonical);
        }

        // Read as text segments and not through `Path::components`, which
        // silently drops a `.` and reads `//` as one root — so the two
        // spellings this refusal is most needed for are the two it would never
        // see. `candidate` begins with `/`, so the first segment is empty by
        // construction and is skipped rather than refused.
        for segment in candidate.split('/').skip(1) {
            if segment.is_empty() || segment == "." || segment == ".." {
                return Err(LocalBackupRootError::NotCanonical);
            }
        }

        let path = Path::new(candidate);
        if is_at_or_under(path, AgentPaths::ACCOUNT_HOME_ROOT) {
            return Err(LocalBackupRootError::UnderAccountHomes);
        }

        for root in SITE_ROOTS {
            if is_at_or_under(path, root) {
                return Err(LocalBackupRootError::UnderSiteRoot);
            }
        }

        for root in WORLD_WRITABLE_ROOTS {
            if is_at_or_under(path, root) {
                return Err(LocalBackupRootError::WorldWritableAncestor);
            }
        }

        Ok(Self(path.to_path_buf()))
    }

    /// The validated root, as the operator spelled it.
    #[must_use]
    pub fn as_str(&self) -> &str {
        // The inner path was built from a `&str` and never joined onto, so it
        // is still valid UTF-8; the fallback keeps that fact from being an
        // `unwrap`, which a root process is not allowed to write.
        self.0.to_str().unwrap_or(DEFAULT_LOCAL_BACKUP_ROOT)
    }

    /// The validated root as a path.
    #[must_use]
    pub fn as_path(&self) -> &Path {
        &self.0
    }

    /// Asks the filesystem what this path really is, and refuses it if that is
    /// not itself.
    ///
    /// This is the check `parse` cannot do. A symlink at any component means
    /// the directory the agent writes into is chosen by whoever can rewrite the
    /// link, and following it would put an archive of a customer's database
    /// wherever that is — so a path that resolves elsewhere is refused, not
    /// followed.
    ///
    /// It is deliberately NOT part of `parse`: a value's validity would then
    /// depend on when it was constructed, and the answer can change after the
    /// value exists. Callers resolve immediately before they write, which is
    /// also where ownership and mode are checked against the same inode.
    ///
    /// # Errors
    ///
    /// - [`LocalBackupRootError::SymlinkComponent`] when the canonical path
    ///   differs from the configured one.
    /// - [`LocalBackupRootError::Unresolvable`] when the path cannot be
    ///   canonicalised at all — it does not exist yet, or a component is not
    ///   readable. Reported as unproven rather than as absent: this check says
    ///   what it could observe, and it observed nothing.
    pub fn resolve(&self) -> Result<PathBuf, LocalBackupRootError> {
        resolve_without_symlinks(&self.0)
    }
}

impl Default for LocalBackupRoot {
    /// The root the panel ships with, `/var/backups/maran`.
    ///
    /// Built by hand rather than by `parse` so that the default cannot be a
    /// `Result` a caller has to unwrap — which the agent is not allowed to do.
    /// That the literal it is built from does pass `parse` is what
    /// `the_default_root_parses` states.
    fn default() -> Self {
        Self(PathBuf::from(DEFAULT_LOCAL_BACKUP_ROOT))
    }
}

/// Core of [`LocalBackupRoot::resolve`], with the path injected.
///
/// Split out for the same reason the privileged file walk splits its child
/// bodies: every directory a test can create sits under `/tmp` or under a home,
/// which are exactly the two places `parse` refuses, so a test that had to
/// build a `LocalBackupRoot` first could never reach this code. The split lets
/// the symlink rule be tested against a real filesystem, which is the only way
/// it can be tested at all.
///
/// # Errors
///
/// As documented on [`LocalBackupRoot::resolve`].
fn resolve_without_symlinks(path: &Path) -> Result<PathBuf, LocalBackupRootError> {
    let canonical = path
        .canonicalize()
        .map_err(|_| LocalBackupRootError::Unresolvable)?;

    if canonical != path {
        return Err(LocalBackupRootError::SymlinkComponent);
    }

    Ok(canonical)
}

/// Whether `path` IS `root` or lies beneath it.
///
/// Compared by component and not by text, so `/tmpfoo` is not read as being
/// under `/tmp` — `Path::starts_with` compares whole components, which is the
/// property this helper exists to borrow rather than to reimplement with
/// `str::starts_with`.
fn is_at_or_under(path: &Path, root: &str) -> bool {
    path.starts_with(root)
}

#[cfg(test)]
#[path = "../../tests/validation/system/local_backup_root_tests.rs"]
mod tests;
