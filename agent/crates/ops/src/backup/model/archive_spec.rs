//! Everything `tar` is told, as one value.

use std::path::{Path, PathBuf};

/// The flag that makes `tar` create an archive.
const CREATE: &str = "--create";

/// The flag that names the compressor, by absolute path, as one argv element.
///
/// Decision 1 picked gzip because both families ship it and because an operator
/// with no panel, no network and a rescue shell can still open the result.
/// **This spelling — and not `--gzip` — is the security-relevant half.**
///
/// `tar --gzip` does not compress in-process. It forks `/bin/sh -c "gzip"` and
/// lets the shell resolve the bare name through `PATH`. Measured, GNU tar 1.35:
///
/// ```text
/// execve(<tar>, ["tar", "--create", "--gzip", …])
/// execve(<shell>, [<shell>, "-c", "gzip"])   <- a bare name, resolved on PATH
/// execve(<whatever PATH found>, ["gzip"])
/// ```
///
/// The agent is root, its unit sets no `Environment=PATH=`, and systemd's
/// compiled-in default begins `/usr/local/sbin:/usr/local/bin` — which is where
/// this product installs. A `gzip` dropped there wins the lookup and every
/// customer home and database dump on the host streams through it. That was
/// demonstrated with a fake binary, not argued.
///
/// With the compressor named absolutely, `PATH` is out of the loop: the same
/// shadow attempt leaves the archive written by `/usr/bin/gzip`. `tar` still
/// forks a shell on the WRITE side — that is tar's own implementation, one
/// `execve` below this argv, and it is handed a compile-time constant from the
/// [`DistroAdapter`](maran_distro::DistroAdapter) containing no caller byte, no
/// metacharacter and no name to resolve. On the READ side tar uses no shell at
/// all: it `execve`s the named program directly. Removing the write-side shell
/// would mean the agent piping `tar --file -` into a `gzip` it spawns itself;
/// that trade is weighed and declined in the task report, because it buys
/// nothing over a constant absolute path and costs a bespoke two-process spawn
/// with two exit statuses to reconcile.
const COMPRESS_PROGRAM: &str = "--use-compress-program=";

/// The flag that stores uid/gid as NUMBERS rather than as names.
///
/// A restore happens on a host where the account's uid may differ from the one
/// the archive was taken on, and the panel re-owns what it extracts anyway; a
/// name lookup at create time would put this host's `/etc/passwd` contents into
/// the archive for every file in it.
const NUMERIC_OWNER: &str = "--numeric-owner";

/// The flag that stops `tar` at a mount point.
///
/// Not a performance nicety. This product bind-mounts the SFTP jail
/// (`/var/lib/maran-sftp/<account>`) underneath the account's home, so without
/// this flag every file under it is archived TWICE — once by its own path and
/// once through a mount the account does not own.
const ONE_FILE_SYSTEM: &str = "--one-file-system";

/// The flag that stores a sparse file as its holes rather than as its zeroes.
const SPARSE: &str = "--sparse";

/// The flag that renames members as they are stored.
const TRANSFORM: &str = "--transform";

/// The rename that puts the account's home under the archive's `home/` prefix.
///
/// The members are fed as `.` from inside the home (see [`ArchiveSpec`]), so
/// this rewrites the leading `.` of every stored name — `.` itself into `home`,
/// `./x` into `home/x`, `./.bashrc` into `home/.bashrc`.
///
/// **Two properties of this expression are load-bearing and both were
/// measured** (GNU tar 1.35, transcript in the task report):
///
/// - The trailing `S` flag turns OFF transformation of symlink TARGETS. With
///   tar's defaults a transform rewrites the target of every relative symlink
///   too, so a link pointing at `../alice/sub/f` was stored pointing at
///   `../home/sub/f` — a silent corruption of the customer's data that only a
///   restore would discover. `S` is not tidiness, it is the difference between
///   an archive that restores and one that does not.
/// - The expression names NO account. An earlier form anchored on the account
///   name (`s|^alice|home|`), which quietly also rewrote the archive's OWN
///   `databases/` member for an account called `data` — `^data` matches
///   `databases`, so the member became `homebases`. Feeding the home as `.`
///   removes the account name from the expression entirely, and `^\.` cannot
///   match a member coming from the scratch chunk.
const HOME_TRANSFORM: &str = r"s|^\.|home|S";

/// The flag naming the archive file to write.
const FILE: &str = "--file";

/// The flag that changes directory before the members that follow it.
const CHANGE_DIRECTORY: &str = "-C";

/// The member fed from inside the account's home: the directory itself.
const HOME_MEMBER: &str = ".";

/// The scratch member holding one `.sql` per dumped database.
///
/// The archive's internal layout is fixed and is part of the contract (R2):
/// `manifest.json`, `home/…`, `databases/…`, and nothing else.
const DATABASES_MEMBER: &str = "databases";

/// The scratch member holding the manifest.
const MANIFEST_MEMBER: &str = "manifest.json";

/// What one `tar` invocation is asked to do, as data rather than as an argv a
/// caller assembled.
///
/// The argv is built by [`ArchiveSpec::arguments`] and by nothing else, which
/// is what makes "every flag this product depends on is present, and `-h` never
/// is" a property a unit test can state once instead of a property each call
/// site has to be read for.
///
/// **There is no `--dereference`/`-h`, and there never will be.** With it, an
/// account that replaces a home subdirectory with a symlink to `/etc` gets
/// `/etc/shadow` into an archive an administrator can download. A symlink is
/// stored as a symlink; that is Decision 1 and it is why this struct has no
/// field that could turn it on.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ArchiveSpec {
    /// The account's home directory — `tar` is entered into it and fed `.`.
    pub home: PathBuf,

    /// The root-only scratch holding `databases/` and `manifest.json`.
    pub scratch: PathBuf,

    /// The file `tar` writes. Always the `.partial` name: the published
    /// artifact only ever comes into existence by a rename.
    pub artifact: PathBuf,
}

impl ArchiveSpec {
    /// The complete argv, program excluded, in the order `tar` reads it.
    ///
    /// Owned `String`s rather than borrowed slices because two of them are
    /// paths that have to be turned into text somewhere, and doing it once here
    /// keeps the lossy conversion in one place.
    ///
    /// A path that is not valid UTF-8 is turned into text lossily rather than
    /// refused: every path in this spec is built by `AgentPaths` from a
    /// validated account name and a validated backup id, so a lossy conversion
    /// here is unreachable — and a `tar` that then fails to open a mangled name
    /// is a failed backup rather than a backup of the wrong thing.
    ///
    /// # The compressor is a parameter and not a field
    ///
    /// `compressor` is the absolute path of `gzip`, and it comes from the
    /// caller — which is the host, holding what
    /// [`DistroAdapter::gzip_binary`](maran_distro::DistroAdapter::gzip_binary)
    /// answered at construction, exactly as it already holds `tar`'s own path.
    /// A field would have to be filled at every construction site, and those
    /// sites are operations deep in this crate that hold no adapter; threading
    /// a platform fact through them is how one call site eventually fills it
    /// with something else. The SHAPE of the flag stays here, in the one
    /// reviewed argv builder; WHICH binary stays where every other program path
    /// in this area already lives. The `COMPRESS_PROGRAM` constant above
    /// carries what the spelling is defending against.
    #[must_use]
    pub fn arguments(&self, compressor: &str) -> Vec<String> {
        vec![
            CREATE.to_owned(),
            format!("{COMPRESS_PROGRAM}{compressor}"),
            NUMERIC_OWNER.to_owned(),
            ONE_FILE_SYSTEM.to_owned(),
            SPARSE.to_owned(),
            TRANSFORM.to_owned(),
            HOME_TRANSFORM.to_owned(),
            FILE.to_owned(),
            text(&self.artifact),
            CHANGE_DIRECTORY.to_owned(),
            text(&self.home),
            HOME_MEMBER.to_owned(),
            CHANGE_DIRECTORY.to_owned(),
            text(&self.scratch),
            DATABASES_MEMBER.to_owned(),
            MANIFEST_MEMBER.to_owned(),
        ]
    }
}

/// A path as text, lossily — see [`ArchiveSpec::arguments`] for why lossily is
/// the right answer here.
fn text(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

#[cfg(test)]
#[path = "../../tests/backup/archive_spec_tests.rs"]
mod tests;
