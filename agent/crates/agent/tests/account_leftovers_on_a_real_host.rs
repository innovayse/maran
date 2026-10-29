//! What an account leaves behind OUTSIDE its home, against a real `userdel`, a real `at`, real SysV
//! IPC and the real shared temporary directories (issue #139).
//!
//! `userdel` removes the account and its home and nothing else. A hosting account's processes also
//! write where no home is involved, and everything below stays owned by the freed numeric uid —
//! which `useradd` hands to the next account it creates:
//!
//! - files and symlinks in `/tmp`, `/var/tmp` and `/dev/shm`;
//! - the mail spool `/var/mail/<name>`, keyed by NAME;
//! - a pending `at` job, which runs as whoever holds the uid when its time comes;
//! - SysV shared memory, message queues and semaphore sets, which persist until removed.
//!
//! Claimed here and nowhere else: after the deletion, none of those exists, a new account handed
//! the SAME uid inherits none of them, and the same sweep does not touch what a NEIGHBOURING
//! account owns.
//!
//! **Two guards against a vacuous pass.** The planting is observed before the deletion (a probe
//! that cannot see what was planted would also see "nothing" afterwards), and the new account is
//! required to have received the same uid as the deleted one (a different uid inherits nothing
//! whatever the deletion did). The neighbour test is the inverse control: a sweep mutated to remove
//! everything in those directories passes the first test and fails the second.
//!
//! **Blind spot, stated where it is measured.** The deletion runs IN-PROCESS here, outside
//! `maran-agent.service`, so none of the unit's sandbox applies: not `PrivateTmp=true`, not
//! `PrivateDevices=true`, not the `BindPaths=` that put the host's shared directories at
//! `/run/maran-host/{tmp,var-tmp,dev-shm}`. To give the sweep something to walk at those paths this
//! suite bind-mounts the three host directories there itself, the way the unit does. That makes the
//! sweep observable; it does NOT show the unit's own bind mounts work. That is observed in the
//! systemd lane (`scripts/lib/install-polygon.sh`), which joins the running agent's mount namespace
//! and requires a marker planted in the host's `/tmp` to be visible at `/run/maran-host/tmp`.
//!
//! These tests need `docker run --privileged` (bind mounts) and `at` in the image.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// This suite uses only part of the shared fixture; the other suites use the rest of it. The
// allow is on the module rather than on the fixture, so a fixture item no suite uses is still
// reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;

use std::os::unix::fs::{MetadataExt, chown, lchown, symlink};
use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::db::ProcessDbHost;
use maran_ops::ftps::ProcessFtpsHost;
use maran_ops::php::ProcessPhpHost;
use maran_ops::sftp::ProcessSftpHost;

use polygon_account::PolygonAccount;

/// The account the deletion runs on.
const ACCOUNT: &str = "polyleft";

/// The neighbouring account whose objects must survive the deletion.
const NEIGHBOUR: &str = "polyleftn";

/// The host's shared temporary directories and where the agent's unit binds each of them.
///
/// The right-hand paths are literals on purpose: they are the layout the installer builds and the
/// unit mounts, and an expectation read from the code under test agrees with it by construction.
const SHARED_DIRECTORIES: [(&str, &str); 3] = [
    ("/tmp", "/run/maran-host/tmp"),
    ("/var/tmp", "/run/maran-host/var-tmp"),
    ("/dev/shm", "/run/maran-host/dev-shm"),
];

/// The mail spool directory, the same path on both families (a symlink to the spool on RHEL).
const MAIL_DIRECTORY: &str = "/var/mail";

/// The line the planted mail spool carries, searched for in the reborn account's spool.
const PLANTED_MAIL_MARKER: &str = "private to the previous tenant";

/// A time far enough away that no `atd` in the image runs the planted job during the test.
const AT_TIME: &str = "203512312359";

/// The distribution adapter of this polygon image.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// Runs the full account deletion under test against the real machine.
///
/// # Panics
///
/// Panics when the deletion refuses, quoting what refused.
fn delete_the_account(name: &AccountName) {
    AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro())
        .delete(
            &ProcessPhpHost::new(),
            &ProcessDbHost::new(polygon_distro()),
            &ProcessSftpHost::new(),
            &ProcessFtpsHost::new(),
            name,
        )
        .unwrap_or_else(|error| panic!("the account deletion must succeed: {error}"));
}

/// Bind mounts of the host's shared directories at the paths the agent's unit sees them at,
/// unmounted again when dropped, including on a panic.
struct HostBinds {
    /// The mount points this guard created a mount on.
    mounted: Vec<PathBuf>,
}

impl HostBinds {
    /// Mounts every shared directory at its `/run/maran-host/...` path.
    ///
    /// # Panics
    ///
    /// Panics when a mount point cannot be made or `mount --bind` refuses (the container was
    /// started without `--privileged`).
    fn mount() -> Self {
        let mut mounted = Vec::new();
        for (host, bound) in SHARED_DIRECTORIES {
            std::fs::create_dir_all(bound).expect("the mount point can be created");
            let status = Command::new("mount")
                .args(["--bind", host, bound])
                .status()
                .expect("mount (util-linux) is in the polygon image");
            assert!(
                status.success(),
                "mount --bind {host} {bound} refused: the container needs --privileged"
            );
            mounted.push(PathBuf::from(bound));
        }
        Self { mounted }
    }
}

impl Drop for HostBinds {
    /// Unmounts what [`HostBinds::mount`] mounted, newest first.
    fn drop(&mut self) {
        for bound in self.mounted.iter().rev() {
            let _ = Command::new("umount").arg(bound).status();
        }
    }
}

/// Every entry directly inside `directory` whose owner is `uid`, symlinks judged by their own
/// owner and never followed.
///
/// # Panics
///
/// Panics when the directory cannot be read.
fn entries_owned_by(directory: &str, uid: u32) -> Vec<PathBuf> {
    std::fs::read_dir(directory)
        .unwrap_or_else(|error| panic!("{directory} must be readable: {error}"))
        .filter_map(Result::ok)
        .map(|entry| entry.path())
        .filter(|path| {
            std::fs::symlink_metadata(path)
                .map(|metadata| metadata.uid() == uid)
                .unwrap_or(false)
        })
        .collect()
}

/// Plants, owned by the account, a file and a symlink in each shared directory and a mail spool
/// file, returning how many entries it made per shared directory.
///
/// # Panics
///
/// Panics when any of them cannot be created or handed to the account.
fn plant_files(account: &PolygonAccount) -> usize {
    let ids = account.ids();
    let tag = account.name().as_str();
    for (directory, _) in SHARED_DIRECTORIES {
        let file = Path::new(directory).join(format!("{tag}-file"));
        std::fs::write(&file, b"left behind by the previous tenant").expect("plant a file");
        chown(&file, Some(ids.uid()), Some(ids.gid())).expect("hand the file to the account");

        let link = Path::new(directory).join(format!("{tag}-link"));
        let _ = std::fs::remove_file(&link);
        // The target is deliberately somewhere the account does not own: a sweep that FOLLOWED
        // the link would touch /etc/hostname, and that file's owner is not the uid.
        symlink("/etc/hostname", &link).expect("plant a symlink");
        lchown(&link, Some(ids.uid()), Some(ids.gid())).expect("hand the link to the account");
    }

    let mail = Path::new(MAIL_DIRECTORY).join(tag);
    std::fs::write(&mail, format!("From: someone\n\n{PLANTED_MAIL_MARKER}\n")).expect("plant mail");
    chown(&mail, Some(ids.uid()), Some(ids.gid())).expect("hand the mail to the account");
    2
}

/// Runs `program` with `arguments` as `account`'s uid and gid, returning its standard output and
/// standard error joined.
///
/// # Panics
///
/// Panics when `setpriv` cannot be run or the program exits unsuccessfully.
fn run_as(account: &PolygonAccount, program: &str, arguments: &[&str], stdin: &[u8]) -> String {
    use std::io::Write;
    use std::process::Stdio;

    let mut child = Command::new("setpriv")
        .arg(format!("--reuid={}", account.ids().uid()))
        .arg(format!("--regid={}", account.ids().gid()))
        .arg("--clear-groups")
        .arg(program)
        .args(arguments)
        .env("LC_ALL", "C")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap_or_else(|error| panic!("{program} must start: {error}"));
    child
        .stdin
        .take()
        .expect("stdin is piped")
        .write_all(stdin)
        .expect("write the program's input");
    let output = child.wait_with_output().expect("wait for the program");
    let said = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(
        output.status.success(),
        "{program} as the account failed: {said}"
    );
    said
}

/// The number that follows `marker` in `text`, e.g. the id after `id:` in ipcmk's output.
///
/// # Panics
///
/// Panics when no number follows the marker.
fn number_after(text: &str, marker: &str) -> String {
    text.split(marker)
        .nth(1)
        .and_then(|rest| rest.split_whitespace().next())
        .filter(|token| token.chars().all(|c| c.is_ascii_digit()))
        .unwrap_or_else(|| panic!("no number after {marker:?} in {text:?}"))
        .to_string()
}

/// Queues one `at` job as the account and returns its id.
///
/// # Panics
///
/// Panics when `at` refuses or its answer carries no job id.
fn plant_at_job(account: &PolygonAccount) -> String {
    let said = run_as(account, "at", &["-t", AT_TIME], b"true\n");
    number_after(&said, "job ")
}

/// Whether the host's `at` queue lists the job `id`, asked of `atq` as root.
///
/// # Panics
///
/// Panics when `atq` cannot be run.
fn at_queue_holds(id: &str) -> bool {
    let output = Command::new("atq")
        .env("LC_ALL", "C")
        .output()
        .expect("atq is in the polygon image");
    String::from_utf8_lossy(&output.stdout)
        .lines()
        .any(|line| line.split_whitespace().next() == Some(id))
}

/// The three SysV object kinds the suite plants: (ipcmk flag, ipcs flag, output marker).
const IPC_KINDS: [(&[&str], &str, &str); 3] = [
    (&["-M", "4096"], "-m", "Shared memory id:"),
    (&["-Q"], "-q", "Message queue id:"),
    (&["-S", "1"], "-s", "Semaphore id:"),
];

/// Creates one object of each SysV kind as the account, returning `(ipcs flag, id)` for each.
///
/// # Panics
///
/// Panics when `ipcmk` refuses.
fn plant_ipc(account: &PolygonAccount) -> Vec<(&'static str, String)> {
    IPC_KINDS
        .iter()
        .map(|(make, list, marker)| {
            let said = run_as(account, "ipcmk", make, b"");
            (*list, number_after(&said, marker))
        })
        .collect()
}

/// Whether the object `id` of the kind `list` (an `ipcs` flag) still exists.
///
/// `ipcs -i` exits 0 whether or not the object exists (measured on util-linux 2.39: a missing
/// id prints `ipcs: id N not found` to standard error and nothing to standard output, status 0),
/// so the answer is whether it described the object on standard output.
///
/// # Panics
///
/// Panics when `ipcs` cannot be run.
fn ipc_exists(list: &str, id: &str) -> bool {
    let output = Command::new("ipcs")
        .args([list, "-i", id])
        .env("LC_ALL", "C")
        .output()
        .expect("ipcs is in the polygon image");
    output.status.success() && !output.stdout.is_empty()
}

/// Removes the SysV objects a failed assertion would otherwise leave on the host.
fn remove_ipc(objects: &[(&'static str, String)]) {
    for (list, id) in objects {
        let remove_flag = match *list {
            "-m" => "--shmem-id",
            "-q" => "--queue-id",
            _ => "--semaphore-id",
        };
        let _ = Command::new("ipcrm").args([remove_flag, id]).status();
    }
}

#[test]
#[ignore = "creates and deletes a real account and plants real objects in shared directories: polygon only"]
fn a_deleted_accounts_shared_directory_files_mail_at_jobs_and_ipc_are_not_inherited_by_the_next_holder_of_its_uid()
 {
    PolygonAccount::require_polygon();
    let _binds = HostBinds::mount();

    let account = PolygonAccount::create(ACCOUNT);
    let name = account.name().clone();
    let uid = account.ids().uid();

    let per_directory = plant_files(&account);
    let job = plant_at_job(&account);
    let ipc = plant_ipc(&account);

    // Positive controls: every probe below must be able to SEE what was planted, or "nothing
    // afterwards" would also be the answer of a probe that is blind.
    for (directory, bound) in SHARED_DIRECTORIES {
        assert!(
            entries_owned_by(directory, uid).len() >= per_directory,
            "the planted entries must be visible in {directory} before the deletion"
        );
        assert!(
            entries_owned_by(bound, uid).len() >= per_directory,
            "the planted entries must be visible at {bound}, the agent's view of {directory}"
        );
    }
    assert!(
        Path::new(MAIL_DIRECTORY).join(name.as_str()).exists(),
        "the planted mail spool must exist before the deletion"
    );
    assert!(
        at_queue_holds(&job),
        "the planted at job {job} must be queued before the deletion"
    );
    for (list, id) in &ipc {
        assert!(
            ipc_exists(list, id),
            "the planted SysV object {id} ({list}) must exist"
        );
    }

    delete_the_account(&name);

    let reborn = PolygonAccount::create(ACCOUNT);
    // The vacuity guard. A different uid inherits nothing whatever the deletion did, so without
    // this line a passing run could be a run in which the question was never asked.
    assert_eq!(
        reborn.ids().uid(),
        uid,
        "useradd did not hand the freed uid to the new account, so this run measured nothing"
    );

    for (directory, _) in SHARED_DIRECTORIES {
        assert!(
            entries_owned_by(directory, uid).is_empty(),
            "entries owned by the freed uid survive in {directory}: {:?}",
            entries_owned_by(directory, uid)
        );
    }
    // The spool is judged by its CONTENT, not by its owner or its existence. On the RHEL family
    // `useradd` creates an empty spool for the reborn account, owned by the same uid; and other
    // polygon suites in the same container leave spools of accounts that held this uid before,
    // which this test did not plant. Only the planted message would be an inheritance.
    let reborn_spool =
        std::fs::read(Path::new(MAIL_DIRECTORY).join(name.as_str())).unwrap_or_default();
    assert!(
        !String::from_utf8_lossy(&reborn_spool).contains(PLANTED_MAIL_MARKER),
        "the new account inherited the previous tenant's mail spool"
    );
    let survived_job = at_queue_holds(&job);
    let survived_ipc: Vec<_> = ipc
        .iter()
        .filter(|(list, id)| ipc_exists(list, id))
        .collect();
    // Cleaned before asserting, so a failing run does not leave the objects on the host.
    remove_ipc(&ipc);
    assert!(
        !survived_job,
        "the at job {job} of the deleted account is still queued"
    );
    assert!(
        survived_ipc.is_empty(),
        "SysV objects of the deleted account survive: {survived_ipc:?}"
    );
    println!(
        "UNOBSERVED HERE: the unit's own BindPaths= (this suite mounts the three directories \
         itself, outside PrivateTmp and PrivateDevices); observed in the systemd lane of \
         scripts/lib/install-polygon.sh"
    );
}

#[test]
#[ignore = "creates and deletes real accounts and plants real objects in shared directories: polygon only"]
fn deleting_an_account_leaves_a_neighbours_shared_directory_files_at_jobs_and_ipc_untouched() {
    PolygonAccount::require_polygon();
    let _binds = HostBinds::mount();

    // The neighbour first, so the account deleted is the one with the higher uid.
    let neighbour = PolygonAccount::create(NEIGHBOUR);
    let account = PolygonAccount::create(ACCOUNT);
    assert_ne!(
        neighbour.ids().uid(),
        account.ids().uid(),
        "the two accounts must differ in uid or the control is not a control"
    );

    plant_files(&neighbour);
    let neighbour_job = plant_at_job(&neighbour);
    let neighbour_ipc = plant_ipc(&neighbour);
    // The deleted account's own objects, planted too: if the sweep did nothing at all, the
    // survival below would be a pass of a deletion that removed nothing.
    plant_files(&account);
    let own_job = plant_at_job(&account);
    let own_ipc = plant_ipc(&account);

    delete_the_account(&account.name().clone());

    let neighbour_uid = neighbour.ids().uid();
    let own_files_gone = SHARED_DIRECTORIES
        .iter()
        .all(|(directory, _)| entries_owned_by(directory, account.ids().uid()).is_empty());
    let own_gone = own_files_gone
        && !at_queue_holds(&own_job)
        && own_ipc.iter().all(|(list, id)| !ipc_exists(list, id));
    let survivors_files: Vec<usize> = SHARED_DIRECTORIES
        .iter()
        .map(|(directory, _)| entries_owned_by(directory, neighbour_uid).len())
        .collect();
    let neighbour_job_queued = at_queue_holds(&neighbour_job);
    let neighbour_ipc_kept = neighbour_ipc.iter().all(|(list, id)| ipc_exists(list, id));
    let neighbour_mail = Path::new(MAIL_DIRECTORY).join(NEIGHBOUR).exists();

    // Cleaned before asserting, so a failing run does not leave the objects on the host.
    remove_ipc(&neighbour_ipc);
    remove_ipc(&own_ipc);

    assert!(
        own_gone,
        "the deleted account's own objects must be gone, or the sweep did nothing and the \
         neighbour's survival below proves nothing"
    );
    assert!(
        survivors_files.iter().all(|count| *count >= 2),
        "the neighbour's files and symlinks must survive in every shared directory: {survivors_files:?}"
    );
    assert!(neighbour_job_queued, "the neighbour's at job must survive");
    assert!(
        neighbour_ipc_kept,
        "the neighbour's SysV objects must survive"
    );
    assert!(neighbour_mail, "the neighbour's mail spool must survive");
}
