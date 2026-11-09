//! The hostile tree a customer can build inside their own home, planted for a
//! browse test.
//!
//! The planting is done AS ROOT, with ordinary `std::fs` calls, and every entry
//! is then handed to the account: the polygon is the one place a test may build
//! the layout a real customer builds with a shell — a link into `/etc`, a FIFO
//! nobody writes to, a second name for a file the account does not own — and the
//! agent under test is then asked about it through its real socket. Nothing
//! here is a fake of the thing under test; it is only the input.
//!
//! Every method names what it plants, so a test reads as the attack it stages.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use std::fs::File;
use std::os::unix::ffi::OsStrExt as _;
use std::os::unix::fs::{lchown, symlink};
use std::os::unix::net::UnixListener;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{Duration, SystemTime};

use super::polygon_account::PolygonAccount;

/// Mode of every directory the tree creates: the account may do anything, the
/// web server group may traverse, nobody else may look.
const DIRECTORY_MODE: u32 = 0o750;

/// A tree being built inside one polygon account's home.
///
/// Holds the account only to know its home and ids; it owns nothing, so
/// dropping it removes nothing — the account's own removal takes the tree with
/// the home.
pub struct PolygonHomeTree<'a> {
    /// The account whose home the tree lives in.
    account: &'a PolygonAccount,
}

impl<'a> PolygonHomeTree<'a> {
    /// Starts planting inside `account`'s home.
    pub fn new(account: &'a PolygonAccount) -> Self {
        Self { account }
    }

    /// The absolute path of `relative` inside the home. Planting never
    /// validates it: a test is allowed to name anything, including a path the
    /// agent must refuse.
    pub fn path(&self, relative: &str) -> PathBuf {
        self.account.home().join(relative)
    }

    /// Hands `path` itself (never what a link points at) to the account.
    pub fn give_to_account(&self, path: &Path) {
        self.give_to(path, self.account.ids().uid(), self.account.ids().gid());
    }

    /// Hands `path` itself to `uid`:`gid`.
    pub fn give_to(&self, path: &Path, uid: u32, gid: u32) {
        lchown(path, Some(uid), Some(gid))
            .unwrap_or_else(|error| panic!("lchown {path:?} to {uid}:{gid}: {error}"));
    }

    /// Creates `relative` and every missing level on the way, each owned by the
    /// account. Returns the absolute path of the last level.
    pub fn directory(&self, relative: &str) -> PathBuf {
        let mut current = self.account.home().to_path_buf();
        for component in relative.split('/').filter(|part| !part.is_empty()) {
            current.push(component);
            if !current.exists() {
                std::fs::create_dir(&current)
                    .unwrap_or_else(|error| panic!("mkdir {current:?}: {error}"));
                std::fs::set_permissions(
                    &current,
                    std::os::unix::fs::PermissionsExt::from_mode(DIRECTORY_MODE),
                )
                .unwrap();
                self.give_to_account(&current);
            }
        }

        current
    }

    /// Plants a regular file holding `bytes`, owned by the account, creating
    /// the directories above it.
    pub fn file(&self, relative: &str, bytes: &[u8]) -> PathBuf {
        let target = self.path(relative);
        if let Some(parent) = Path::new(relative).parent() {
            self.directory(&parent.to_string_lossy());
        }
        std::fs::write(&target, bytes).unwrap_or_else(|error| panic!("write {target:?}: {error}"));
        self.give_to_account(&target);

        target
    }

    /// Plants a file of `length` bytes that costs no disk: sparse, so reading it
    /// back yields zeros.
    pub fn sparse_file(&self, relative: &str, length: u64) -> PathBuf {
        let target = self.file(relative, b"");
        File::options()
            .write(true)
            .open(&target)
            .unwrap()
            .set_len(length)
            .unwrap();

        target
    }

    /// Plants a symbolic link at `relative` pointing at `target` (any string,
    /// dangling or not), owned by the account.
    pub fn symlink(&self, relative: &str, target: &str) -> PathBuf {
        let link = self.path(relative);
        if let Some(parent) = Path::new(relative).parent() {
            self.directory(&parent.to_string_lossy());
        }
        symlink(target, &link).unwrap_or_else(|error| panic!("symlink {link:?}: {error}"));
        self.give_to_account(&link);

        link
    }

    /// Plants a FIFO nobody ever opens for writing: an `open(2)` of it for
    /// reading blocks for ever unless the opener asked not to wait.
    pub fn fifo(&self, relative: &str) -> PathBuf {
        let path = self.path(relative);
        let made = Command::new("/usr/bin/mkfifo")
            .arg("--")
            .arg(&path)
            .status()
            .expect("mkfifo can be started");
        assert!(made.success(), "mkfifo failed to make {path:?}");
        self.give_to_account(&path);

        path
    }

    /// Plants a character device node (`/dev/null`'s numbers), owned by the
    /// account. Needs `CAP_MKNOD`, which a default container has.
    pub fn device(&self, relative: &str) -> PathBuf {
        let path = self.path(relative);
        let made = Command::new("/usr/bin/mknod")
            .arg(&path)
            .args(["c", "1", "3"])
            .status()
            .expect("mknod can be started");
        assert!(made.success(), "mknod failed to make {path:?}");
        self.give_to_account(&path);

        path
    }

    /// Plants a unix socket file, owned by the account. The listener is
    /// returned so the caller can keep it alive; the file outlives it either way.
    pub fn socket(&self, relative: &str) -> (PathBuf, UnixListener) {
        let path = self.path(relative);
        let listener = UnixListener::bind(&path)
            .unwrap_or_else(|error| panic!("bind a socket at {path:?}: {error}"));
        self.give_to_account(&path);

        (path, listener)
    }

    /// Plants a hard link at `relative` to `existing`, which lives anywhere on
    /// the same filesystem. The link's inode keeps its owner.
    pub fn hard_link(&self, relative: &str, existing: &Path) -> PathBuf {
        let link = self.path(relative);
        if let Some(parent) = Path::new(relative).parent() {
            self.directory(&parent.to_string_lossy());
        }
        std::fs::hard_link(existing, &link)
            .unwrap_or_else(|error| panic!("hard link {existing:?} -> {link:?}: {error}"));

        link
    }

    /// Plants `count` empty files named `<prefix><n>` directly under `directory`
    /// (an absolute path). They are left owned by root: the agent only
    /// `lstat`s them, and chowning a hundred thousand inodes would dominate the
    /// run for nothing the test observes.
    pub fn many_empty_files(&self, directory: &Path, prefix: &str, range: std::ops::Range<u32>) {
        for index in range {
            File::create(directory.join(format!("{prefix}{index}")))
                .unwrap_or_else(|error| panic!("create {prefix}{index} in {directory:?}: {error}"));
        }
    }

    /// Plants `count` empty files whose names are exactly 255 bytes long — the
    /// longest a name may be — so a listing of them is as heavy as a listing of
    /// that many entries can be.
    pub fn many_longest_names(&self, directory: &Path, count: u32) {
        for index in 0..count {
            File::create(directory.join(format!("{index:0>255}")))
                .unwrap_or_else(|error| panic!("create a 255-byte name in {directory:?}: {error}"));
        }
    }

    /// Plants an empty file whose name is the raw bytes `name` (which need not be
    /// UTF-8), owned by the account.
    pub fn file_named_by_bytes(&self, directory: &Path, name: &[u8]) -> PathBuf {
        let path = directory.join(std::ffi::OsStr::from_bytes(name));
        File::create(&path).unwrap_or_else(|error| panic!("create {path:?}: {error}"));
        self.give_to_account(&path);

        path
    }

    /// Sets the modification time of `path` to `seconds` after the epoch.
    pub fn set_modified(&self, path: &Path, seconds: u64) {
        File::options()
            .write(true)
            .open(path)
            .unwrap()
            .set_modified(SystemTime::UNIX_EPOCH + Duration::from_secs(seconds))
            .unwrap();
    }
}
