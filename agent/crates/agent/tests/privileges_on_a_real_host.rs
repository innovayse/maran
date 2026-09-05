//! The privilege drop, against the kernel rather than against a fake.
//!
//! `fork_as_account` is the single most security-critical function in the
//! workspace and the only place `unsafe` is allowed, and none of what it does
//! can be observed without root and a real account: the ids come from
//! `getpwnam_r`, the drop is three syscalls, and the evidence that it worked is
//! what the kernel says afterwards. So this suite runs in the polygon, as root,
//! against an account it creates through the same `useradd` the agent runs in
//! production.
//!
//! What each test proves is stated on the test itself. Two properties are worth
//! naming here because they are the reason the module has the shape it has:
//! the child ends up with the account's uid, gid AND supplementary group list,
//! and it cannot get root back.
//!
//! Every wait is bounded. `fork_as_account` blocks in `waitpid` with no timeout
//! of its own, so a protection removed from the child would otherwise hang this
//! suite instead of failing it — and a hang is read as a flaky runner and
//! retried, which is how a removed protection survives a test run.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;

use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _};
use std::path::{Path, PathBuf};
use std::sync::mpsc;
use std::time::Duration;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::fork_as_account::fork_as_account;
use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::web::domain::Domain;
use maran_ops::files::{DeleteEntryInput, FilesOpError, ProcessFilesHost, WriteFileInput};
use maran_ops::sites::{LogSink, ProcessSiteHost, SiteLogKind, SitesOpError, TailEnd};

use polygon_account::PolygonAccount;

/// How long any single forked child is given before the test declares it stuck.
///
/// Everything under test here is three syscalls and a `mkdir`, so seconds are
/// generous. The number matters less than its existence: without it, deleting a
/// check from the child turns a red test into a hung one.
const CHILD_TIMEOUT: Duration = Duration::from_secs(30);

/// Runs `body` on its own thread and fails the test if it outlasts
/// [`CHILD_TIMEOUT`].
///
/// The thread is abandoned rather than killed on a timeout — a forked child
/// blocked in `waitpid` cannot be reclaimed — which is acceptable because the
/// panic ends the run.
///
/// # Panics
///
/// Panics when `body` does not finish in time, or panics itself.
fn within<T: Send + 'static>(what: &str, body: impl FnOnce() -> T + Send + 'static) -> T {
    let (sender, receiver) = mpsc::channel();
    std::thread::spawn(move || {
        // A send failure means the receiver timed out and is gone; the value is
        // simply dropped.
        let _ = sender.send(body());
    });

    match receiver.recv_timeout(CHILD_TIMEOUT) {
        Ok(value) => value,
        Err(_) => panic!("{what} did not finish within {CHILD_TIMEOUT:?}"),
    }
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_child_dropped_to_the_account_writes_as_the_account_and_not_as_root() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsone");
    let ids = account.ids();
    let target = account.home().join("created-by-the-child");
    let created = target.clone();

    let outcome = within("the directory-creating child", move || {
        fork_as_account(&ids, || {
            std::fs::create_dir(&created).map_err(|_| PrivError::WorkFailed)
        })
    });

    assert_eq!(outcome, Ok(()));

    // The evidence is the inode, not the return value: a drop that did not take
    // effect would produce exactly the same `Ok(())` and a root-owned directory.
    let metadata = std::fs::metadata(&target).expect("the child must have created the directory");
    assert_eq!(
        metadata.uid(),
        ids.uid(),
        "the directory must belong to the account"
    );
    assert_eq!(metadata.gid(), ids.gid(), "and to the account's own group");
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_dropped_child_cannot_get_root_back() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivstwo");
    let ids = account.ids();
    let marker = account.home().join("root-was-refused");
    let evidence = marker.clone();

    let outcome = within("the child that asks for root back", move || {
        fork_as_account(&ids, || {
            // A dropped process that can still become root is not dropped. The
            // kernel must refuse this, and the marker is written only when it
            // does — so a success leaves no marker and the assertion below
            // fails rather than passing on a missing negative.
            if rustix::thread::set_thread_uid(rustix::process::Uid::ROOT).is_ok() {
                return Err(PrivError::WorkFailed);
            }
            std::fs::write(&evidence, b"refused").map_err(|_| PrivError::WorkFailed)
        })
    });

    assert_eq!(outcome, Ok(()), "the child must refuse root and carry on");
    assert!(
        marker.exists(),
        "the refusal must have been observed, not assumed"
    );
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_child_does_not_inherit_the_daemons_supplementary_groups() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsthree");
    let ids = account.ids();

    // The daemon's own thread takes an extra supplementary group first, so the
    // fork inherits one. This is the state the ordering exists for: `setuid`
    // gives away the capability needed to clear it, so a child that dropped the
    // uid first would keep this group forever.
    //
    // `set_thread_groups` is per-thread on Linux, so no other test's thread is
    // affected by it.
    rustix::thread::set_thread_groups(&[
        rustix::process::Gid::ROOT,
        rustix::process::Gid::from_raw(ids.gid()),
    ])
    .expect("root may set its own supplementary groups");

    let expected = ids.gid();
    let outcome = within("the child inspecting its own groups", move || {
        fork_as_account(&ids, || {
            let groups = rustix::process::getgroups().map_err(|_| PrivError::WorkFailed)?;
            // Exactly the primary group, or none: the same two shapes a correct
            // `setgroups([gid])` can leave. The child's own verification looks
            // at the same list and refuses first, so this is the second net and
            // not an independent one — either way the test goes red, and the
            // list is what the assertion is about rather than the exit status.
            if groups.iter().any(|group| group.as_raw() != expected) {
                return Err(PrivError::WorkFailed);
            }
            Ok(())
        })
    });

    assert_eq!(
        outcome,
        Ok(()),
        "root's supplementary groups must not survive into the child"
    );
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_dropped_child_does_not_inherit_the_daemons_open_descriptors() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsnine");
    let ids = account.ids();

    // A descriptor the parent holds across the fork, standing in for what the
    // daemon really holds there: its listening unix socket, every accepted
    // connection of every other tenant, and its log. `fork` copies the whole
    // table and this module never `exec`s, so `O_CLOEXEC` closes none of it —
    // only the child's own sweep does.
    let inherited = std::fs::File::open("/etc/hostname").expect("the polygon has a hostname file");
    let raw = std::os::fd::AsRawFd::as_raw_fd(&inherited);
    // Built before the fork: the child does the least it can, and this is a
    // string the parent can just as well hand it.
    let probe = PathBuf::from(format!("/proc/self/fd/{raw}"));
    let marker = account.home().join("descriptors-were-closed");
    let evidence = marker.clone();

    let outcome = within("the child inspecting its descriptor table", move || {
        fork_as_account(&ids, move || {
            // Written only when the inherited descriptor is gone, so a sweep that
            // stopped happening leaves no marker and fails the assertion below
            // rather than passing on a missing negative.
            if probe.exists() {
                return Err(PrivError::WorkFailed);
            }
            std::fs::write(&evidence, b"closed").map_err(|_| PrivError::WorkFailed)
        })
    });

    // The parent's own copy is still open, which is what makes the child's
    // answer about the child rather than about the file.
    assert!(
        PathBuf::from(format!("/proc/self/fd/{raw}")).exists(),
        "the parent must still hold the descriptor it opened"
    );
    drop(inherited);

    assert_eq!(
        outcome,
        Ok(()),
        "the child must not still hold the daemon's descriptors"
    );
    assert!(
        marker.exists(),
        "the closure must have observed the closed descriptor, not been assumed to"
    );
}

#[test]
#[ignore = "inspects the image's own toolchain: polygon only"]
fn the_polygon_toolchain_is_not_writable_by_the_accounts_the_drop_tests_fork_into() {
    PolygonAccount::require_polygon();

    // The suite forks children into real unprivileged accounts inside this
    // image, and the toolchain those children could reach is the image's own
    // cargo and rustup homes. Group- or world-writable, they are a place for a
    // dropped child to leave something the next `cargo test` in the image runs
    // as root. The Dockerfiles do `chmod -R go-w` on both; this is what makes
    // that line load-bearing rather than decorative.
    for path in [
        "/usr/local/cargo",
        "/usr/local/rustup",
        "/usr/local/cargo/bin/cargo",
    ] {
        let mode = std::fs::metadata(path)
            .unwrap_or_else(|_| panic!("{path} must exist in the polygon image"))
            .mode();
        assert_eq!(
            mode & 0o022,
            0,
            "{path} must not be writable by group or other, got mode {mode:o}"
        );
    }
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_child_that_is_already_unprivileged_cannot_drop_again() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsfour");
    let ids = account.ids();

    // The inner fork runs in a process that has already dropped, so its
    // `setgroups` is refused for want of CAP_SETGID — which is the DropFailed
    // path, reached the only way it can be reached without breaking the
    // machine. The outer child reports the inner verdict by succeeding only
    // when the inner call failed the way it must.
    let outcome = within("the nested drop", move || {
        fork_as_account(&ids, || match fork_as_account(&ids, || Ok(())) {
            Err(PrivError::DropFailed) => Ok(()),
            _ => Err(PrivError::WorkFailed),
        })
    });

    assert_eq!(
        outcome,
        Ok(()),
        "an unprivileged process must not be able to run the drop sequence"
    );
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_child_killed_by_a_signal_is_reported_as_signalled_and_not_as_success() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsfive");
    let ids = account.ids();

    let outcome = within("the child that kills itself", move || {
        fork_as_account(&ids, || {
            // Half a write is a state the operation above has to converge from,
            // so the caller must learn that the child died rather than that it
            // finished.
            let _ = rustix::process::kill_process(
                rustix::process::getpid(),
                rustix::process::Signal::KILL,
            );
            Ok(())
        })
    });

    assert_eq!(
        outcome,
        Err(PrivError::ChildSignalled {
            signal: rustix::process::Signal::KILL.as_raw()
        })
    );
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_work_closure_that_fails_as_the_account_is_reported_as_work_failed() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivssix");
    let ids = account.ids();

    // Writing into a directory root owns is what a customer's operation looks
    // like when it has correctly lost the right to do so.
    let outcome = within("the child denied by its own uid", move || {
        fork_as_account(&ids, || {
            std::fs::write("/etc/maran/written-by-a-customer", b"no")
                .map_err(|_| PrivError::WorkFailed)
        })
    });

    assert_eq!(outcome, Err(PrivError::WorkFailed));
    assert!(
        !PathBuf::from("/etc/maran/written-by-a-customer").exists(),
        "a dropped child must not be able to write where only root may"
    );
}

#[test]
#[ignore = "creates a real system account and drops privileges: polygon only"]
fn a_work_closure_that_panics_is_reported_as_work_failed_and_never_unwinds_out_of_the_fork() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivseight");
    let ids = account.ids();
    let home = account.home().to_path_buf();

    // A panic must not unwind past the fork: doing so would let a second copy of
    // the caller's stack keep running — here, a second copy of the TEST HARNESS,
    // which would go on to run and report tests from a process nobody knows
    // exists. The child catches the unwind and exits instead.
    let outcome = within("the child that panics", move || {
        fork_as_account(&ids, || {
            let _ = std::fs::write(home.join("reached-after-the-panic"), b"no");
            panic!("a work closure that panics must not escape the child")
        })
    });

    assert_eq!(outcome, Err(PrivError::WorkFailed));
}

#[test]
#[ignore = "creates a real system account: polygon only"]
fn a_real_account_resolves_to_the_ids_the_password_database_holds() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyprivsseven");

    // The lookup against a real `getpwnam_r` entry, which every other test of
    // `AccountIds::resolve` reaches only through its failure paths. The witnesses
    // are independent of the code under test: the home directory's owner is
    // whoever `useradd` chowned it to, and the group id comes from the shadow
    // tooling's own `id -g`.
    let resolved = AccountIds::resolve(account.name()).expect("a real account must resolve");
    let home = std::fs::metadata(account.home()).expect("useradd must have created the home");

    assert_eq!(resolved.uid(), home.uid());
    assert_eq!(resolved.gid(), primary_group_id(account.name().as_str()));
    assert_ne!(resolved.uid(), 0, "a hosting account is never root");
    assert_ne!(resolved.gid(), 0, "nor in root's group");

    // The home's GROUP is deliberately not the account's own any more: creating an
    // account group-owns its home by the web server's group, at mode 0750, so a real
    // nginx can traverse into a document root without the home being opened to every
    // other local user. Asserted here because this is the test that used to read the
    // home's gid as the account's, and the change is a decision rather than a slip.
    assert_ne!(
        home.gid(),
        resolved.gid(),
        "the home is group-owned by the web server so a site under it can be served"
    );
}

#[test]
#[ignore = "creates two real system accounts: polygon only"]
fn a_site_log_owned_by_another_real_account_is_refused() {
    PolygonAccount::require_polygon();
    let owner = PolygonAccount::create("polyprivsowner");
    let stranger = PolygonAccount::create("polyprivsother");
    let domain = Domain::parse("logs.example.test").expect("a valid domain");

    // The log lives at the path the owner's tail would open, and belongs to a
    // different real account — a hardlink to somebody else's file, planted
    // where the customer's own log goes. Every path check passes; only the
    // inode's uid says no.
    let logs = owner.home().join("logs");
    std::fs::create_dir_all(&logs).expect("the log directory must be creatable");
    // The DIRECTORY must belong to the owner, or the tail refuses at the
    // directory check and never looks at the file — the refusal would then be
    // the right answer for the wrong reason, and the owner check on the log
    // itself would be untested while looking tested.
    chown_to(&logs, &owner);
    let log = logs.join(format!("{}.access.log", domain.as_str()));
    std::fs::write(&log, b"a line the owner never wrote\n").expect("the log must be writable");
    chown_to(&log, &stranger);

    let name = owner.name().clone();
    let tailed = domain.clone();
    // The refusal is immediate, so this call cannot reach the follow loop's
    // idle ceiling; the bound is here anyway, because a test that relied on the
    // refusal to end the wait would hang — not fail — if the refusal were
    // removed, and the follow loop's own ceiling is five minutes.
    let (refusal, delivered) = within("the tail of a stranger's log", move || {
        let mut sink = CountingSink::default();
        let outcome = maran_ops::sites::tail_site_log(
            &ProcessSiteHost::new(),
            &name,
            &tailed,
            SiteLogKind::Access,
            10,
            &mut sink,
        );
        (outcome, sink.lines())
    });

    assert!(
        matches!(refusal, Err(SitesOpError::LogUnreadable { .. })),
        "a log owned by another account must be refused, got {refusal:?}"
    );
    assert_eq!(
        delivered, 0,
        "not one line of another account's file may reach the client"
    );
}

/// Gives `path` to `account`, so a test can plant a file owned by somebody else.
///
/// Done with `chown` on an existing file rather than by writing as that account,
/// because what is under test is the reader's reaction to the owner, not how the
/// file came to have one.
fn chown_to(path: &std::path::Path, account: &PolygonAccount) {
    let file = std::fs::File::open(path).expect("the planted log must be openable");
    rustix::fs::fchown(
        &file,
        Some(rustix::process::Uid::from_raw(account.ids().uid())),
        Some(rustix::process::Gid::from_raw(account.ids().gid())),
    )
    .expect("root may give a file away");
}

/// A sink that counts what it was handed and always claims to be listening.
///
/// The tail under test never delivers a line — it is refused before the file is
/// read — and the count is asserted only to make that explicit rather than
/// assumed.
#[derive(Default)]
struct CountingSink {
    /// How many lines were delivered.
    lines: usize,
}

impl LogSink for CountingSink {
    /// Counts the line and accepts it.
    fn line(&mut self, _line: &str, _historical: bool) -> Result<(), TailEnd> {
        self.lines += 1;
        Ok(())
    }

    /// Always listening: nothing here decides to stop a tail.
    fn is_listening(&mut self) -> bool {
        true
    }
}

impl CountingSink {
    /// How many lines this sink was handed.
    fn lines(&self) -> usize {
        self.lines
    }
}

#[test]
#[ignore = "creates a real system account and writes into its home: polygon only"]
fn an_acme_challenge_is_written_into_a_real_home_as_the_account_and_removed_again() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyfilesone");
    let ids = account.ids();
    let name = account.name().clone();
    let home = account.home().to_path_buf();

    let path = RelativePath::parse("sites/files.example.test/.well-known/acme-challenge/token123")
        .expect("the challenge path must be valid");
    let written_path = path.clone();
    let removed_path = path.clone();
    let removing = name.clone();

    // Bounded like every other wait here: `write_file` forks twice and blocks
    // in `waitpid` with no timeout of its own, so a protection removed from the
    // child would hang this suite rather than fail it.
    let written = within("the challenge write", move || {
        maran_ops::files::write_file(
            &ProcessFilesHost::new(),
            &WriteFileInput {
                account: name,
                path: written_path,
                contents: b"token123.key-authorization".to_vec(),
                mode: FileMode::parse(0o644).expect("a plain permission mode"),
            },
        )
    });
    assert_eq!(written, Ok(26));

    // The evidence is the inode. A write that ran as root would leave exactly
    // the same content and a file the customer cannot replace or delete — and
    // one the panel would report as a success.
    let file = home.join("sites/files.example.test/.well-known/acme-challenge/token123");
    let metadata = std::fs::metadata(&file).expect("the challenge must exist");
    assert_eq!(
        metadata.uid(),
        ids.uid(),
        "the file must belong to the account"
    );
    assert_eq!(metadata.gid(), ids.gid(), "and to its primary group");
    assert_eq!(
        metadata.mode() & 0o777,
        0o644,
        "the web server has to read it"
    );
    assert_eq!(
        std::fs::read(&file).expect("the challenge must be readable"),
        b"token123.key-authorization"
    );

    // Every directory the write created belongs to the account too: a
    // root-owned `.well-known` inside a customer's document root is one they
    // can neither empty nor remove.
    for level in [
        "sites",
        "sites/files.example.test",
        "sites/files.example.test/.well-known",
        "sites/files.example.test/.well-known/acme-challenge",
    ] {
        assert_eq!(
            std::fs::metadata(home.join(level))
                .expect("each level must exist")
                .uid(),
            ids.uid(),
            "the level {level} must belong to the account"
        );
    }

    let removed = within("the challenge removal", move || {
        maran_ops::files::delete_entry(
            &ProcessFilesHost::new(),
            &DeleteEntryInput {
                account: removing,
                path: removed_path,
            },
        )
    });
    assert_eq!(removed, Ok(()));
    assert!(
        !file.exists(),
        "the single-use proof must not be left behind"
    );
}

#[test]
#[ignore = "creates two real system accounts: polygon only"]
fn a_challenge_directory_owned_by_another_real_account_is_refused() {
    PolygonAccount::require_polygon();
    let owner = PolygonAccount::create("polyfilesowner");
    let stranger = PolygonAccount::create("polyfilesother");

    // The level below the home belongs to somebody else — the one case a unit
    // test cannot build, because it cannot chown. Everything above it is the
    // owner's, so the home check passes and the LEVEL's ownership check is what
    // has to refuse; without it the write would proceed into another account's
    // directory.
    let sites = owner.home().join("sites");
    std::fs::create_dir_all(&sites).expect("the directory must be creatable");
    chown_to(&sites, &stranger);
    // World-writable ON PURPOSE, and it is what makes this test test what it
    // says. Left at 0755 the owner's forked child cannot create inside the
    // stranger's directory anyway, so the refusal comes from the permission
    // bits and the OWNERSHIP check is never reached — the test would be green
    // with that check deleted. At 0777 the walk is free to proceed and only the
    // `metadata.uid() != uid` comparison can stop it.
    std::fs::set_permissions(&sites, std::fs::Permissions::from_mode(0o777))
        .expect("root may widen a directory it is planting");

    let name = owner.name().clone();
    let path = RelativePath::parse("sites/files.example.test/.well-known/acme-challenge/token123")
        .expect("the challenge path must be valid");

    let refused = within("the write into a stranger's directory", move || {
        maran_ops::files::write_file(
            &ProcessFilesHost::new(),
            &WriteFileInput {
                account: name,
                path,
                contents: b"token".to_vec(),
                mode: FileMode::parse(0o644).expect("a plain permission mode"),
            },
        )
    });

    assert_eq!(
        refused,
        Err(FilesOpError::DirectoryUnusable),
        "a level owned by another account must be refused"
    );
    assert!(
        !sites.join("files.example.test").exists(),
        "nothing may be created inside another account's directory"
    );
}

#[test]
#[ignore = "creates two real system accounts: polygon only"]
fn a_challenge_file_owned_by_another_real_account_is_not_removed() {
    PolygonAccount::require_polygon();
    let owner = PolygonAccount::create("polyfilesfown");
    let stranger = PolygonAccount::create("polyfilesfoth");

    // The ENTRY's ownership check, which no unit test can reach: a test cannot
    // `chown`, and handing the removal a foreign uid makes the HOME check fire
    // first. The walk's own level check is a different one and is covered by
    // `a_challenge_directory_owned_by_another_real_account_is_refused`.
    let directory = owner
        .home()
        .join("sites/files.example.test/.well-known/acme-challenge");
    std::fs::create_dir_all(&directory).expect("the directory must be creatable");
    chown_tree_to(owner.home(), &owner);
    let file = directory.join("token123");
    std::fs::write(&file, b"a token the owner never asked for").expect("writable");
    chown_to(&file, &stranger);

    // Everything above the file belongs to the owner and is writable by them, so
    // the unlink WOULD succeed on permissions alone — a directory's write bit is
    // what unlink is checked against, not the file's owner. Only
    // `metadata.uid() != uid` can refuse this.
    let name = owner.name().clone();
    let path = RelativePath::parse("sites/files.example.test/.well-known/acme-challenge/token123")
        .expect("the challenge path must be valid");

    let refused = within("the removal of a stranger's file", move || {
        maran_ops::files::delete_entry(
            &ProcessFilesHost::new(),
            &DeleteEntryInput {
                account: name,
                path,
            },
        )
    });

    assert_eq!(
        refused,
        Err(FilesOpError::RemoveFailed),
        "a file belonging to another account must not be unlinked"
    );
    assert!(file.exists(), "and it must still be there");
}

#[test]
#[ignore = "creates a real system account: polygon only"]
fn a_challenge_that_is_already_gone_is_reported_as_not_found() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polyfilesgone");

    let directory = account
        .home()
        .join("sites/files.example.test/.well-known/acme-challenge");
    std::fs::create_dir_all(&directory).expect("the directory must be creatable");
    chown_tree_to(account.home(), &account);

    // The one refusal the root-side `resolve_in_home` is genuinely the only
    // producer of, driven against a real filesystem and a real privilege drop.
    // The forked child cannot answer it: its outcome is an exit status, so every
    // child-side refusal comes back as `RemoveFailed`. Removing the resolve
    // therefore turns this red — which is what makes step 2 of the write's
    // sibling operation an honest check rather than a claim.
    let name = account.name().clone();
    let path = RelativePath::parse("sites/files.example.test/.well-known/acme-challenge/token123")
        .expect("the challenge path must be valid");

    let answer = within("the removal of a challenge that is not there", move || {
        maran_ops::files::delete_entry(
            &ProcessFilesHost::new(),
            &DeleteEntryInput {
                account: name,
                path,
            },
        )
    });

    assert_eq!(answer, Err(FilesOpError::NotFound));
}

/// Gives `root` and everything under it to `account`.
///
/// `create_dir_all` runs as root here, so the levels it makes belong to root and
/// the account's own forked child could neither traverse nor write them. A test
/// whose fixture the code under test cannot use proves nothing about the code.
fn chown_tree_to(root: &Path, account: &PolygonAccount) {
    chown_to(root, account);
    let Ok(entries) = std::fs::read_dir(root) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            chown_tree_to(&path, account);
        } else {
            chown_to(&path, account);
        }
    }
}

/// The primary group id the shadow tooling reports for `username`.
///
/// An independent witness: it comes from `id`, not from the `getpwnam_r` wrapper
/// under test, so the two agreeing means something.
///
/// # Panics
///
/// Panics when `id` is unavailable or prints something that is not a number,
/// which on a polygon image it never is.
fn primary_group_id(username: &str) -> u32 {
    let outcome = std::process::Command::new("id")
        .args(["-g", username])
        .output()
        .expect("the polygon image has the shadow tooling");

    assert!(
        outcome.status.success(),
        "id -g must answer for an account useradd has just created"
    );

    String::from_utf8_lossy(&outcome.stdout)
        .trim()
        .parse::<u32>()
        .expect("id -g prints a number")
}
