//! The backup archiver, run for real, with a hostile `gzip` first on `PATH`.
//!
//! This suite exists because of a defect no other test could see. `tar --gzip`
//! does not compress in its own process: it forks `/bin/sh -c "gzip"` and lets
//! the shell resolve the bare name through `PATH`. The agent is root, its unit
//! pins no `PATH`, and systemd's compiled-in default begins
//! `/usr/local/sbin:/usr/local/bin` — the directory this product installs into.
//! A `gzip` placed there compressed every customer home and every database dump
//! on the host, and produced a perfectly valid archive while doing it, so every
//! assertion any test made about the ARTIFACT passed either way.
//!
//! The fix names the compressor by the absolute path the distro adapter already
//! measured (`gzip_binary()`), which takes `PATH` out of the lookup. The unit
//! tests in `maran-ops` assert on the argv, which is where the change is
//! visible without spawning anything; this suite asserts the consequence, by
//! running the real [`ProcessBackupHost`] with the attack in place and checking
//! that the impostor never ran.
//!
//! What it does NOT settle: the create side still passes through `tar`'s own
//! internal `/bin/sh -c "<absolute path>"`, which is tar's implementation and
//! not an argv this product builds. That shell is handed a compile-time
//! constant with no caller byte in it and no name to resolve. The read side
//! uses no shell at all — tar `execve`s the named program directly.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, create_dir_all, read_to_string, write};
use std::os::unix::fs::PermissionsExt as _;
use std::path::{Path, PathBuf};

use maran_agent_core::utils::available_bytes::available_bytes;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::backup::model::archive_spec::ArchiveSpec;
use maran_ops::backup::model::extract_spec::ExtractSpec;
use maran_ops::backup::{ArchivePart, BackupHost as _, ProcessBackupHost};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The contents of the one file the fixture home holds.
///
/// Round-tripped through create and extract and compared byte for byte, so the
/// suite proves the archive is a real archive and not merely a file that exists.
const HOME_FILE_CONTENTS: &str = "the customer's own bytes\n";

/// Refuses to go on unless this process is inside a polygon image.
///
/// A panic and not a quiet `return`: this suite is `#[ignore]`d, so the only way
/// to reach it is to ask for it by name, and a skip would then report as a pass
/// — which is the exact failure this suite exists to end (rules/testing.md: "no
/// tests found" is a failure, never a pass).
///
/// # Panics
///
/// Panics when the polygon marker is absent.
fn require_polygon() {
    let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
    assert!(
        !marker.is_empty(),
        "this suite runs the real archiver against a real host of a supported \
         family, and must run inside a polygon container: {POLYGON_MARKER} is \
         not set. See docker/README.md."
    );
}

/// The adapter for the family this container actually is.
///
/// Detection rather than a hard-coded adapter, so the suite exercises the same
/// pairing the agent makes at startup.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The variable the outer half of each case uses to hand the inner half the
/// directory it must work in.
///
/// The two halves are two PROCESSES, and the reason is `rules/rust.md`: poisoning
/// this process's own `PATH` would mean `std::env::set_var`, which is `unsafe`,
/// and `privs` is the only place in this workspace where `unsafe` is allowed. A
/// child spawned with `Command::env` needs none, and it is also the more honest
/// model of the attack — the agent inherits a `PATH` it did not choose, exactly
/// as this child does.
const FIXTURE_VARIABLE: &str = "MARAN_TEST_FIXTURE_ROOT";

/// A home, a scratch and a place for the artifact, laid out as a real backup
/// finds them.
///
/// The scratch holds the two members the archive's fixed layout requires
/// (`manifest.json` and `databases/`), because a spec that names a member the
/// directory does not hold is a `tar` failure and would fail this suite for the
/// wrong reason.
enum Fixture {
    /// The outer half's tree, removed when the fixture drops.
    Owned(tempfile::TempDir),
    /// The inner half's view of a tree the outer half owns.
    Borrowed(PathBuf),
}

impl Fixture {
    /// Builds the tree.
    fn new() -> Self {
        let root = tempfile::tempdir().expect("a temporary directory");
        let fixture = Self::Owned(root);

        create_dir_all(fixture.home()).expect("the home");
        write(fixture.home().join("file.txt"), HOME_FILE_CONTENTS).expect("the home's file");

        create_dir_all(fixture.scratch().join("databases")).expect("the dumps directory");
        write(fixture.scratch().join("databases/one.sql"), "SELECT 1;\n").expect("a dump");
        write(fixture.scratch().join("manifest.json"), "{}\n").expect("the manifest");

        create_dir_all(fixture.path("out")).expect("the extraction directory");
        fixture
    }

    /// The tree an outer half built, when this process IS the inner half.
    ///
    /// `None` in the outer half, and that is how each case knows which half it
    /// is. One test that re-enters itself, rather than a pair of tests, so that
    /// a blanket `--ignored` run — which is how the polygon suites are actually
    /// invoked — never reaches an inner half with no tree and reports it as a
    /// failure of the product.
    fn inherited() -> Option<Self> {
        let root = std::env::var(FIXTURE_VARIABLE).unwrap_or_default();
        (!root.is_empty()).then(|| Self::Borrowed(PathBuf::from(root)))
    }

    /// The fixture's root directory.
    fn root(&self) -> &Path {
        match self {
            Self::Owned(directory) => directory.path(),
            Self::Borrowed(path) => path.as_path(),
        }
    }

    /// A path under the fixture root.
    fn path(&self, name: &str) -> PathBuf {
        self.root().join(name)
    }

    /// The account's home.
    fn home(&self) -> PathBuf {
        self.path("home")
    }

    /// The root-only scratch.
    fn scratch(&self) -> PathBuf {
        self.path("scratch")
    }

    /// The artifact the archiver writes.
    fn artifact(&self) -> PathBuf {
        self.path("artifact.tar.gz")
    }
}

/// Writes an impostor `gzip` into the fixture and answers the directory holding
/// it together with the marker file it touches when it runs.
///
/// The reviewer's own attack, reproduced: a script named exactly `gzip`, which
/// records that it was reached and then does the real work, so that a case
/// failing this way fails on the ASSERTION rather than on a broken archive.
///
/// # Panics
///
/// Panics when the impostor cannot be written or made executable.
fn plant_impostor_gzip(fixture: &Fixture, real_gzip: &str) -> (PathBuf, PathBuf) {
    let directory = fixture.path("impostor");
    create_dir_all(&directory).expect("the impostor's directory");

    let marker = fixture.path("impostor-ran");
    let script = format!(
        "#!/bin/sh\nprintf 'reached' > {}\nexec {real_gzip} \"$@\"\n",
        marker.display()
    );

    let program = directory.join("gzip");
    write(&program, script).expect("the impostor");
    std::fs::set_permissions(&program, Permissions::from_mode(0o755)).expect("the impostor's mode");

    (directory, marker)
}

/// Runs THIS case again, in a child whose `PATH` begins with `impostor`, and
/// answers what that child reported.
///
/// A child rather than this process, for the reason [`FIXTURE_VARIABLE`] gives.
/// The child is this same test binary — `current_exe` — asked for this same case
/// by name, so there is no second artifact to keep in step and no second case a
/// blanket run could reach on its own. The child sees the fixture variable and
/// therefore takes the other branch. The polygon marker is passed through
/// explicitly: without it the child's own `require_polygon` would refuse, which
/// is the correct refusal in the wrong process.
///
/// # Panics
///
/// Panics when the child cannot be started.
fn run_under_impostor_path(case: &str, fixture: &Fixture, impostor: &Path) -> std::process::Output {
    let inherited = std::env::var("PATH").unwrap_or_default();
    let polygon = std::env::var(POLYGON_MARKER).unwrap_or_default();

    std::process::Command::new(std::env::current_exe().expect("this test binary"))
        .args([
            case,
            "--exact",
            "--ignored",
            "--nocapture",
            "--test-threads=1",
        ])
        .env("PATH", format!("{}:{inherited}", impostor.display()))
        .env(POLYGON_MARKER, polygon)
        .env(FIXTURE_VARIABLE, fixture.root())
        .output()
        .expect("the child half of this case starts")
}

/// True when the file at `path` begins with the gzip magic bytes.
fn is_gzip(path: &Path) -> bool {
    std::fs::read(path)
        .map(|bytes| bytes.starts_with(&[0x1f, 0x8b]))
        .unwrap_or_default()
}

/// A backup taken with a hostile `gzip` first on `PATH` is compressed by the
/// adapter's binary, and the impostor is never reached.
///
/// The assertion that could not be made before. With `--gzip` this same fixture
/// produced a valid `.tar.gz` AND ran the impostor; the artifact is byte-for-byte
/// unremarkable either way, so the marker file is the only thing that tells the
/// two apart.
///
/// This case runs as two processes and is written as one test that re-enters
/// itself: the child does the archiving under the poisoned `PATH` and can see
/// only that the archiver SUCCEEDED — which is exactly the blindness the defect
/// exploited — while the parent, whose `PATH` is clean, judges the marker file.
#[test]
#[ignore = "requires a polygon container"]
fn a_shadowed_gzip_on_path_never_compresses_a_backup() {
    require_polygon();
    let distro = polygon_distro();

    if let Some(fixture) = Fixture::inherited() {
        let bytes = ProcessBackupHost::new(distro)
            .create_archive(&ArchiveSpec {
                home: fixture.home(),
                scratch: fixture.scratch(),
                artifact: fixture.artifact(),
            })
            .expect("the archiver succeeds with the impostor on PATH");
        assert!(bytes > 0);
        return;
    }

    let fixture = Fixture::new();
    let (impostor, marker) = plant_impostor_gzip(&fixture, distro.gzip_binary());

    let output = run_under_impostor_path(
        "a_shadowed_gzip_on_path_never_compresses_a_backup",
        &fixture,
        &impostor,
    );
    assert!(
        output.status.success(),
        "the archiving half failed:\n{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );

    assert!(
        is_gzip(&fixture.artifact()),
        "the artifact is not a gzip stream"
    );
    assert!(
        !marker.exists(),
        "the impostor on PATH compressed the backup: the argv named a bare \
         program name, not {}",
        distro.gzip_binary()
    );
}

/// Listing and extracting an archive with a hostile `gzip` first on `PATH` reads
/// it with the adapter's binary, and returns the archived bytes unchanged.
///
/// The restore half of the same proposition, and the half that matters more:
/// the artifact a restore reads is the one input to this area that a customer
/// can supply. Two processes, one test, exactly as the create case.
///
/// The round trip is checked in the CHILD rather than the parent because it is
/// the evidence that a decompressor really ran: a listing and an extraction that
/// both succeed and hand back the archived bytes cannot have been a no-op.
#[test]
#[ignore = "requires a polygon container"]
fn a_shadowed_gzip_on_path_never_reads_an_archive_back() {
    require_polygon();
    let distro = polygon_distro();
    let host = ProcessBackupHost::new(distro);

    if let Some(fixture) = Fixture::inherited() {
        let members = host
            .list_members(&fixture.artifact())
            .expect("the listing succeeds with the impostor on PATH");
        assert!(
            members
                .iter()
                .any(|member| member.contains("home/file.txt")),
            "{members:?}"
        );

        host.extract(&ExtractSpec {
            artifact: fixture.artifact(),
            into: fixture.path("out"),
            part: ArchivePart::Databases,
        })
        .expect("the extraction succeeds with the impostor on PATH");

        let restored = read_to_string(fixture.path("out").join("databases/one.sql"))
            .expect("the dump came back out");
        assert_eq!(restored, "SELECT 1;\n");

        let home = read_to_string(fixture.home().join("file.txt")).expect("the home's file");
        assert_eq!(home, HOME_FILE_CONTENTS);
        return;
    }

    let fixture = Fixture::new();
    host.create_archive(&ArchiveSpec {
        home: fixture.home(),
        scratch: fixture.scratch(),
        artifact: fixture.artifact(),
    })
    .expect("the archiver succeeds");

    let (impostor, marker) = plant_impostor_gzip(&fixture, distro.gzip_binary());
    let output = run_under_impostor_path(
        "a_shadowed_gzip_on_path_never_reads_an_archive_back",
        &fixture,
        &impostor,
    );
    assert!(
        output.status.success(),
        "the reading half failed:\n{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );

    assert!(
        !marker.exists(),
        "the impostor on PATH decompressed a customer-supplied artifact: the \
         argv named a bare program name, not {}",
        distro.gzip_binary()
    );
}

// ---------------------------------------------------------------------------
// The whole operation, end to end, against a real account and a real server.
//
// Everything above this line exercises `ProcessBackupHost` — the archiver and
// the decompressor. What follows drives `ops::backup`'s four operations
// themselves against a real hosting account, a real home and a real MariaDB,
// which is the only place three of their propositions can be observed at all:
// the artifact's mode inside a root-only directory, the ownership a restore
// leaves the home in, and a symbolic link inside a customer's home being
// archived AS a link rather than followed.
// ---------------------------------------------------------------------------

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// `run_as` — the fixture's "can this credential still log in" probe — belongs
// to the database suite's claims and is not one of this suite's, so it is
// unused here. The allow is on the module rather than on the fixture, so a
// fixture item no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_mariadb.rs"]
mod polygon_mariadb;

use std::fs::{metadata, remove_file, symlink_metadata};
use std::io::Write as _;
use std::os::unix::fs::MetadataExt as _;
use std::os::unix::fs::symlink;
use std::process::{Command, Stdio};

use maran_agent::services::backup::db_host_catalog::DbHostCatalog;
use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::group_id::GroupId;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::{
    BackupError, BackupStage, ProgressSink, RestoreSink, RestoreStage, create_backup,
    delete_backup, list_backups, restore_backup,
};
use maran_ops::db::{CreateDatabaseRequest, ProcessDbHost, create_database};

use polygon_account::PolygonAccount;
use polygon_mariadb::PolygonMariadb;

use std::sync::Arc;

/// The password the fixture database's user is created with.
const FIXTURE_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The id every backup in these cases is created under.
const FIXTURE_BACKUP_ID: &str = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

/// The string the symlink proposition plants in a real file under the home.
///
/// It is the POSITIVE CONTROL for the grep that then looks for `/etc/shadow`'s
/// bytes: the same search, over the same decompressed stream, must FIND this —
/// otherwise "shadow's bytes are absent" would be a claim made by a probe that
/// can see nothing at all (rules/testing.md).
const PLANTED_MARKER: &str = "maran-planted-canary-string";

/// A sink that records the stages an operation reported, in order.
///
/// Recorded rather than ignored so a case can assert that the work it claims to
/// have watched actually reported the stage it was watching for — an operation
/// that emitted nothing and returned `Ok` would otherwise look identical.
#[derive(Default)]
struct RecordingSink {
    /// The stage names, in the order they were reported.
    stages: Vec<String>,
}

impl ProgressSink for RecordingSink {
    fn report(&mut self, stage: BackupStage, _percent: u32) {
        self.stages.push(stage.as_str().to_owned());
    }
}

impl RestoreSink for RecordingSink {
    fn report(&mut self, stage: RestoreStage, _percent: u32) {
        self.stages.push(stage.as_str().to_owned());
    }
}

/// The backup id these cases use, validated.
fn fixture_id() -> BackupId {
    BackupId::parse(FIXTURE_BACKUP_ID).expect("the fixture's id is a uuid")
}

/// The root every case writes into: the agent's own.
fn root() -> LocalBackupRoot {
    LocalBackupRoot::default()
}

/// The directory a backup of `account` lands in.
fn account_directory(account: &AccountName) -> PathBuf {
    root().as_path().join(account.as_str())
}

/// The artifact `id` publishes under for `account`.
fn artifact_of(account: &AccountName, id: &BackupId) -> PathBuf {
    account_directory(account).join(format!("{}.tar.gz", id.as_str()))
}

/// The sidecar beside that artifact.
fn sidecar_of(account: &AccountName, id: &BackupId) -> PathBuf {
    account_directory(account).join(format!("{}.json", id.as_str()))
}

/// Removes everything a previous run left for `account`, without using the
/// code under test to do it.
fn clear_backups(account: &AccountName) {
    let directory = account_directory(account);
    let _ = std::fs::remove_dir_all(&directory);
}

/// Creates one real database for `account` and puts a row in it.
///
/// Returns the database's validated name. The row is what makes the restore
/// case observable: a database that is merely present proves nothing about
/// whether its CONTENTS came back.
fn give_account_a_database(server: &PolygonMariadb, account: &AccountName) -> DatabaseName {
    let database = DatabaseName::for_account(account, "shop").expect("a valid database name");
    let name = database.as_str().to_owned();

    server.run(&format!("DROP DATABASE IF EXISTS `{name}`"));
    server.run(&format!("DROP USER IF EXISTS '{name}'@'localhost'"));

    create_database(
        &ProcessDbHost::new(polygon_distro()),
        &CreateDatabaseRequest {
            database: database.clone(),
            user: DbUserName::for_account(account, "shop").expect("a valid user name"),
            password: Password::parse(FIXTURE_PASSWORD).expect("a valid password"),
        },
    )
    .unwrap_or_else(|error| panic!("creating the fixture database must succeed: {error}"));

    server.run(&format!(
        "CREATE TABLE `{name}`.`orders` (note VARCHAR(64)); \
         INSERT INTO `{name}`.`orders` VALUES ('before-the-backup')"
    ));

    database
}

/// The single note the fixture table holds, as the real client reads it.
fn note_in(server: &PolygonMariadb, database: &DatabaseName) -> String {
    let output = server.run(&format!(
        "SELECT note FROM `{}`.`orders`",
        database.as_str()
    ));
    String::from_utf8_lossy(&output.stdout).trim().to_owned()
}

/// Gives `account` a real login password, so `/etc/shadow` holds a real hash.
///
/// The symlink proposition looks for shadow's bytes inside the archive, and it
/// needs a byte string worth looking for. A polygon image ships with every
/// account's password field set to `*` — no account has ever logged in — so
/// without this the suite's own precondition ("the polygon's shadow file holds
/// at least one hash") is false, which is how it was measured failing on both
/// images. Planting the hash here makes the probe's target a real crypt string
/// this run put there, rather than something the base image was assumed to
/// carry.
///
/// The account is a throwaway the fixture removes at the end of the case, and
/// the password never leaves this file.
fn give_account_a_password(account: &AccountName) {
    let mut child = Command::new("chpasswd")
        .stdin(Stdio::piped())
        .spawn()
        .expect("chpasswd runs on a polygon host");
    child
        .stdin
        .take()
        .expect("chpasswd was spawned with a pipe on its input")
        .write_all(format!("{}:{FIXTURE_PASSWORD}\n", account.as_str()).as_bytes())
        .expect("the credential is written to chpasswd");
    let status = child.wait().expect("chpasswd is waited for");
    assert!(status.success(), "chpasswd must set the password: {status}");
}

/// The catalog the creation asks which databases the account owns.
fn catalog() -> DbHostCatalog<ProcessDbHost> {
    DbHostCatalog::new(Arc::new(ProcessDbHost::new(polygon_distro())))
}

/// `<user>:<group>:<mode>` of `path`, as `stat` reports it.
fn stat_of(path: &Path) -> String {
    let output = Command::new("stat")
        .args(["-c", "%U:%G:%a"])
        .arg(path)
        .output()
        .expect("stat runs on a polygon host");
    String::from_utf8_lossy(&output.stdout).trim().to_owned()
}

/// The SHA-256 `sha256sum` reports for `path`.
///
/// A second, independent measurement: comparing the agent's recorded digest
/// against the agent's own hasher would pass however wrong both were.
fn sha256sum_of(path: &Path) -> String {
    let output = Command::new("sha256sum")
        .arg(path)
        .output()
        .expect("sha256sum runs on a polygon host");
    String::from_utf8_lossy(&output.stdout)
        .split_whitespace()
        .next()
        .unwrap_or_default()
        .to_owned()
}

/// Every member `tar tzf` lists inside `artifact`.
fn members_of(artifact: &Path) -> String {
    let output = Command::new("tar")
        .arg("tzf")
        .arg(artifact)
        .output()
        .expect("tar runs on a polygon host");
    String::from_utf8_lossy(&output.stdout).into_owned()
}

/// The decompressed archive as bytes, for a probe that has to look INSIDE it.
fn decompressed(artifact: &Path) -> Vec<u8> {
    let output = Command::new("tar")
        .args(["xzOf"])
        .arg(artifact)
        .output()
        .expect("tar runs on a polygon host");
    output.stdout
}

#[test]
#[ignore = "creates a real account, a real database and a real artifact: polygon only"]
fn a_backup_of_a_real_account_publishes_a_root_only_artifact_whose_digest_is_the_files_own() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybackone");
    clear_backups(account.name());
    let database = give_account_a_database(&server, account.name());

    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    let mut sink = RecordingSink::default();
    let summary = create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut sink,
    )
    .unwrap_or_else(|error| panic!("a backup of a real account must succeed: {error}"));

    let artifact = artifact_of(account.name(), &fixture_id());
    assert!(artifact.exists(), "the artifact was not published");

    // Root only, both of them: the artifact is a copy of every file in a
    // customer's home and a dump of every database they own.
    assert_eq!(stat_of(&artifact), "root:root:600");
    assert_eq!(stat_of(&account_directory(account.name())), "root:root:700");

    let members = members_of(&artifact);
    for expected in ["manifest.json", "home/", "databases/"] {
        assert!(
            members.contains(expected),
            "the archive's layout is missing {expected}:\n{members}"
        );
    }
    assert!(
        members.contains(&format!("databases/{}.sql", database.as_str())),
        "the account's database is not in the archive:\n{members}"
    );

    let details = summary
        .readable_details()
        .expect("a completed creation describes itself");
    assert_eq!(
        details.artifact_sha256,
        sha256sum_of(&artifact),
        "the recorded digest is not the digest of the published file"
    );
    assert_eq!(
        details.artifact_bytes,
        metadata(&artifact).expect("the artifact").len()
    );

    // The sink saw the work: an operation that reported no stage at all would
    // otherwise be indistinguishable from this one.
    assert!(sink.stages.iter().any(|stage| stage == "dumping_databases"));
    assert!(sink.stages.iter().any(|stage| stage == "archiving_files"));
    // ...and never the stage that belongs to a destination this agent cannot
    // reach. A local creation uploads nothing.
    assert!(!sink.stages.iter().any(|stage| stage == "uploading"));
}

#[test]
#[ignore = "creates a real account and a real artifact: polygon only"]
fn a_symbolic_link_in_a_home_is_archived_as_a_link_and_never_followed() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybacktwo");
    clear_backups(account.name());
    give_account_a_database(&server, account.name());

    // The hash the probe below hunts for must exist before it is hunted: a
    // polygon image sets no account password, so this run plants one.
    give_account_a_password(account.name());

    // The control: a real file under the home holding a known string, so the
    // probe below is proved able to see the inside of the archive at all.
    write(account.home().join("canary.txt"), PLANTED_MARKER).expect("the planted file");
    symlink("/etc/shadow", account.home().join("link")).expect("the hostile link");

    create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .unwrap_or_else(|error| panic!("a backup of a home holding a link must succeed: {error}"));

    let artifact = artifact_of(account.name(), &fixture_id());
    let listed = Command::new("tar")
        .arg("tvzf")
        .arg(&artifact)
        .output()
        .expect("tar runs on a polygon host");
    let listing = String::from_utf8_lossy(&listed.stdout).into_owned();
    assert!(
        listing.contains("-> /etc/shadow"),
        "the link was not archived as a link:\n{listing}"
    );

    let contents = decompressed(&artifact);
    let planted = PLANTED_MARKER.as_bytes();
    assert!(
        contents
            .windows(planted.len())
            .any(|window| window == planted),
        "the probe cannot see inside the archive, so its next assertion would \
         prove nothing"
    );

    let shadow = read_to_string("/etc/shadow").expect("root reads the shadow file");
    let secret = shadow
        .lines()
        .find_map(|line| {
            let hash = line.split(':').nth(1)?;
            (hash.len() > 8).then(|| hash.to_owned())
        })
        .expect("the polygon's shadow file holds at least one hash");
    let secret = secret.as_bytes();
    assert!(
        !contents
            .windows(secret.len())
            .any(|window| window == secret),
        "the archive holds bytes from /etc/shadow: the link was followed"
    );
}

#[test]
#[ignore = "restores a real account's home and database: polygon only"]
fn a_restore_puts_the_home_and_the_database_back_and_leaves_the_home_as_the_account_owns_it() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybackthree");
    clear_backups(account.name());
    let database = give_account_a_database(&server, account.name());
    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    let summary = create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .unwrap_or_else(|error| panic!("the backup must succeed: {error}"));
    let digest = summary
        .readable_details()
        .expect("a completed creation describes itself")
        .artifact_sha256
        .clone();

    // Now break both halves, the way a customer does.
    write(account.home().join("index.html"), "overwritten\n").expect("the mutation");
    remove_file(account.home().join("index.html")).ok();
    server.run(&format!(
        "UPDATE `{}`.`orders` SET note = 'after-the-backup'",
        database.as_str()
    ));
    assert_eq!(note_in(&server, &database), "after-the-backup");

    let group = GroupId::resolve(polygon_distro().web_server_group())
        .expect("the polygon has the web server's group")
        .gid();
    let mut sink = RecordingSink::default();
    let outcome = restore_backup(
        &ProcessBackupHost::new(polygon_distro()),
        account.name(),
        &fixture_id(),
        &root(),
        std::slice::from_ref(&database),
        &digest,
        group,
        &mut sink,
    )
    .unwrap_or_else(|error| panic!("the restore must succeed: {error}"));

    // The verdict is stated by comparing the fields, never by one flag.
    assert!(outcome.files_restored);
    assert_eq!(outcome.databases_restored, outcome.databases_total);
    assert_eq!(outcome.databases_total, 1);

    assert_eq!(
        read_to_string(account.home().join("index.html")).expect("the file is back"),
        HOME_FILE_CONTENTS
    );
    assert_eq!(note_in(&server, &database), "before-the-backup");

    // The arrangement `AccountOperations` creates, and the one a restore is
    // most likely to undo silently: owned by the account, group-owned by the
    // web server's group so it can traverse, 0750.
    assert_eq!(
        stat_of(account.home()),
        format!(
            "{}:{}:750",
            account.name().as_str(),
            polygon_distro().web_server_group()
        )
    );
    let restored_file = metadata(account.home().join("index.html")).expect("the restored file");
    assert_eq!(
        restored_file.uid(),
        account.ids().uid(),
        "a restored file is owned by the account, not by root"
    );
}

#[test]
#[ignore = "unpacks a hostile archive as root: polygon only"]
fn an_archive_naming_a_member_outside_the_layout_is_refused_and_writes_nothing() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybackfour");
    clear_backups(account.name());
    let database = give_account_a_database(&server, account.name());
    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .unwrap_or_else(|error| panic!("the backup must succeed: {error}"));

    // The hostile artifact replaces the published one, and the expected digest
    // is taken from the REPLACEMENT — so the checksum gate is satisfied by
    // construction and cannot be what refuses this. Whatever refuses it is a
    // check about the archive's contents, which is the thing under test.
    let staging = tempfile::tempdir().expect("a temporary directory");
    let member = staging.path().join("pwn");
    write(&member, "* * * * * root id\n").expect("the hostile member");

    let artifact = artifact_of(account.name(), &fixture_id());
    let built = Command::new("tar")
        .arg("--create")
        .arg("--gzip")
        .arg("--file")
        .arg(&artifact)
        .arg("--transform")
        .arg("s|^pwn$|../../etc/cron.d/pwn|")
        .arg("--directory")
        .arg(staging.path())
        .arg("pwn")
        .output()
        .expect("tar runs on a polygon host");
    assert!(
        built.status.success(),
        "building the hostile archive failed: {}",
        String::from_utf8_lossy(&built.stderr)
    );

    let planted = Path::new("/etc/cron.d/pwn");
    let _ = remove_file(planted);

    let group = GroupId::resolve(polygon_distro().web_server_group())
        .expect("the polygon has the web server's group")
        .gid();
    let refusal = restore_backup(
        &ProcessBackupHost::new(polygon_distro()),
        account.name(),
        &fixture_id(),
        &root(),
        &[database],
        &sha256sum_of(&artifact),
        group,
        &mut RecordingSink::default(),
    )
    .expect_err("a hostile archive must be refused");

    // Two refusals are correct answers and the case accepts either, because
    // both are checks about the archive rather than about its bytes: the
    // sidecar beside it still describes the archive it was written for, and
    // the member scan refuses a member outside the layout. What is NOT
    // acceptable is a restore that proceeds — which is the next assertion.
    assert!(
        matches!(
            refusal,
            BackupError::ManifestDisagreesWithSidecar
                | BackupError::UnexpectedArchiveMember { .. }
                | BackupError::ManifestVersionUnknown
        ),
        "refused for a reason that is not about the archive: {refusal}"
    );
    assert!(
        symlink_metadata(planted).is_err(),
        "the hostile member was written to {}",
        planted.display()
    );
}

#[test]
#[ignore = "creates a real artifact twice: polygon only"]
fn creating_the_same_backup_twice_reports_already_exists_and_does_not_rewrite_it() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybackfive");
    clear_backups(account.name());
    give_account_a_database(&server, account.name());
    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .unwrap_or_else(|error| panic!("the first backup must succeed: {error}"));

    let artifact = artifact_of(account.name(), &fixture_id());
    let before = metadata(&artifact).expect("the artifact").mtime_nsec();
    let before_seconds = metadata(&artifact).expect("the artifact").mtime();

    let repeated = create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .expect_err("a repeated creation is refused");

    assert!(matches!(repeated, BackupError::AlreadyExists), "{repeated}");
    let after = metadata(&artifact).expect("the artifact");
    assert_eq!(
        (after.mtime(), after.mtime_nsec()),
        (before_seconds, before),
        "the repeated creation rewrote the artifact"
    );
}

#[test]
#[ignore = "deletes a real artifact: polygon only"]
fn deleting_a_backup_removes_both_files_and_deleting_again_reports_not_found() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybacksix");
    clear_backups(account.name());
    give_account_a_database(&server, account.name());
    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    create_backup(
        &ProcessBackupHost::new(polygon_distro()),
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .unwrap_or_else(|error| panic!("the backup must succeed: {error}"));

    // The listing sees it before the delete: the inverse control for the
    // absence asserted afterwards.
    let listed = list_backups(&root(), account.name()).expect("the listing succeeds");
    assert_eq!(listed.len(), 1);
    assert!(
        listed[0].is_readable(),
        "the listing could not read it back"
    );

    delete_backup(&root(), account.name(), &fixture_id()).expect("the delete succeeds");

    assert!(!artifact_of(account.name(), &fixture_id()).exists());
    assert!(!sidecar_of(account.name(), &fixture_id()).exists());
    assert!(
        list_backups(&root(), account.name())
            .expect("the listing succeeds")
            .is_empty()
    );

    let repeated = delete_backup(&root(), account.name(), &fixture_id())
        .expect_err("deleting a backup that is gone reports it");
    assert!(matches!(repeated, BackupError::NotFound), "{repeated}");
}

// --- The scratch ceiling's measurement, checked against a second implementation ---
//
// `available_bytes` is what bounds every dump this suite's operations stage:
// `ops::backup::scratch_dump_ceiling` narrows the create side's cap to nine
// tenths of what the scratch filesystem has left, and
// `ops::backup::require_scratch_room` refuses a restore before the first
// database is dropped when the scratch cannot hold the rollback dumps. Both
// answers are a single number, and which of `statvfs`'s three block counts
// produced it — and which size field multiplied it — is a decision no assertion
// running on an arbitrary machine can observe: on a healthy filesystem every
// count is non-zero and `f_frsize` is not 1, so every wrong choice still answers
// "a positive number". Only a SECOND, independent measurement of the same
// filesystem separates them, which is why this control lives here, in a lane CI
// runs on both families, rather than as an `#[ignore]`d unit test nothing
// executes.

/// How far this helper's answer may differ from `df`'s before the two are
/// answering different questions.
///
/// One percent. They read the same `statvfs` on the same filesystem, so the
/// tolerance covers a write landing between the two readings and nothing else.
/// It is deliberately far tighter than the root reserve that separates
/// `f_bfree` from `f_bavail` — typically 5% — because that difference is
/// exactly what the controls below exist to catch.
const AVAILABLE_TOLERANCE: f64 = 0.01;

/// The filesystem this image is guaranteed to carry without a root reserve.
///
/// A container gets `/dev/shm` as a tmpfs from the engine itself, so the case
/// below OBSERVES a reserve-free filesystem instead of manufacturing one — it
/// mounts nothing, and it refuses to pretend when what it finds is not what it
/// needs.
const RESERVE_FREE_CANDIDATE: &str = "/dev/shm";

/// What `df` says is available on the filesystem `path` sits on, in bytes.
///
/// A second implementation of the same `statvfs` question, written by someone
/// else, which is what makes the comparison evidence rather than a restatement
/// of the code under test.
fn df_available_bytes(path: &Path) -> u64 {
    let df = Command::new("df")
        .args(["-B1", "--output=avail"])
        .arg(path)
        .output()
        .expect("the polygon image installs df");
    assert!(
        df.status.success(),
        "df must answer about {}",
        path.display()
    );

    let printed = String::from_utf8_lossy(&df.stdout);
    printed
        .lines()
        .nth(1)
        .and_then(|line| line.trim().parse().ok())
        .unwrap_or_else(|| panic!("df must print one figure in bytes, printed: {printed:?}"))
}

/// Asserts a reading and `df`'s agree about the same filesystem.
fn assert_agrees_with_df(path: &Path, reported: u64) {
    let available = df_available_bytes(path);
    assert!(available > 0, "df reports no room on {}", path.display());

    let difference = reported.abs_diff(available) as f64 / available as f64;
    assert!(
        difference <= AVAILABLE_TOLERANCE,
        "on {} the helper says {reported}, df says {available}, off by {:.2}%. A gap of about \
         the root reserve is the signature of f_bfree where f_bavail belongs; a gap of about the \
         block size is the signature of f_frsize replaced by 1",
        path.display(),
        difference * 100.0
    );
}

#[test]
#[ignore = "compares this host's statvfs answer against df's: polygon only"]
fn the_root_filesystem_reports_the_bytes_df_says_an_unprivileged_writer_could_add() {
    require_polygon();

    // Both polygon roots are overlay filesystems over ext4, which keeps a
    // reserve for root, so `f_bavail` and `f_bfree` genuinely differ here and
    // `df`'s available column is the one the helper must match.
    let reported = available_bytes(Path::new("/")).expect("the root filesystem must be readable");

    assert_agrees_with_df(Path::new("/"), reported);
}

#[test]
#[ignore = "measures a filesystem that has no root reserve: polygon only"]
fn a_filesystem_with_no_root_reserve_still_reports_what_df_says_is_available() {
    require_polygon();

    // The invariant's blind spot, exercised rather than described. Where a
    // filesystem keeps no reserve, `f_bavail`, `f_bfree` and every ordinary
    // assertion about "a plausible positive number" coincide, and the only
    // wrong choice still visible is the size field. This case pins that one.
    //
    // The precondition is OBSERVED and never manufactured: the mount already
    // exists, and it is accepted only after `statvfs` itself says the two
    // counts coincide. Manufacturing a precondition is what rules/testing.md
    // records as this project's most expensive defect shape.
    let candidate = Path::new(RESERVE_FREE_CANDIDATE);
    let Ok(space) = rustix::fs::statvfs(candidate) else {
        eprintln!(
            "UNOBSERVED HERE: {RESERVE_FREE_CANDIDATE} cannot be measured on this host, so the \
             reserve-free case could not be exercised"
        );
        return;
    };
    if space.f_bavail != space.f_bfree {
        eprintln!(
            "UNOBSERVED HERE: {RESERVE_FREE_CANDIDATE} keeps a root reserve on this host \
             (f_bavail {} against f_bfree {}), so it is not the reserve-free filesystem this \
             case needs",
            space.f_bavail, space.f_bfree
        );
        return;
    }

    let reported = available_bytes(candidate).expect("a mounted tmpfs must be readable");

    assert_agrees_with_df(candidate, reported);

    // Said in the check's own output rather than left for a reader to infer:
    // on this filesystem the available/free choice is invisible by
    // construction, and the case above it is the one that sees it.
    eprintln!(
        "UNOBSERVED HERE: {RESERVE_FREE_CANDIDATE} keeps no root reserve, so f_bavail against \
         f_bfree cannot be distinguished on it; the root filesystem case is what closes that \
         choice"
    );
}

// --- The create side's per-dump ceiling, measured against the filesystem ---
//
// `create_backup` re-measures `ops::backup::scratch_dump_ceiling` for EVERY
// dump, and the number it produces is nine tenths of what the scratch
// filesystem has left at that moment. A mutation that hands the loop the
// constant cap instead — 8 GiB, the same number for every dump and for every
// host — was measured permitting 8.9 times what a 1 GiB scratch could hold, and
// no unit test in this workspace can see it: a filesystem small enough for the
// two answers to differ is a mount, and the local suite is not root. This case
// is the create-side twin of the `df` controls above, and it lives here for the
// same reason they do.
//
// It asserts the VALUE, never a bound. `limit > 0`, `limit <= 8 GiB` and every
// other inequality is satisfied by the mutant, which is exactly how a truncation
// defect elsewhere in this repository passed a test that only checked the answer
// was small enough.

/// The tmpfs the scratch is put on, as a fraction of the dump that must not fit.
///
/// The window is narrow and it is arithmetic, not taste. The refusal this case
/// needs happens when the dump is larger than nine tenths of what the
/// filesystem has (`available * 9 / 10 < dump`) and yet still small enough to
/// be written at all (`dump < available`), so `available` must land inside
/// `(dump, dump * 10 / 9)`. 1.055 is the middle of that interval: about five
/// percent of room either side, which no directory entry or block rounding on a
/// two-megabyte dump can cross.
const SCRATCH_SIZE_PER_MILLE: u64 = 1055;

/// The numerator of the share of free space one dump may spend.
///
/// A deliberate second copy of `ops::backup::scratch_dump_ceiling`'s own
/// constant rather than an import — the private one is the thing under test,
/// and a test that computes its expectation from the value under test asserts
/// nothing at all.
const SPENDABLE_NUMERATOR: u64 = 9;

/// The denominator of [`SPENDABLE_NUMERATOR`].
const SPENDABLE_DENOMINATOR: u64 = 10;

/// The per-database cap `create_backup` applies when the filesystem is roomy.
///
/// Copied for the same reason, and named so the assertion's failure message can
/// say "this is the constant, unnarrowed" rather than print a bare number.
const CONSTANT_DUMP_CEILING: u64 = 8 * 1024 * 1024 * 1024;

/// The smallest dump this case will work with.
///
/// Below about a megabyte the five-percent margins above shrink towards a
/// filesystem block, and the case would start reporting on rounding instead of
/// on the ceiling.
const SMALLEST_USABLE_DUMP: u64 = 1024 * 1024;

/// A tmpfs mounted over the agent's own scratch root for the life of one case.
///
/// The real path, because the ceiling is measured on the filesystem the dumps
/// are really written to and `create_backup` names that path itself. The mount
/// is taken down by `Drop`, so a panicking assertion cannot leave the next case
/// running on a four-megabyte scratch.
struct SmallScratch {
    /// The mount point — [`AgentPaths::BULK_SCRATCH_ROOT`].
    at: PathBuf,
}

impl SmallScratch {
    /// Mounts a tmpfs of `bytes` at `at`, or answers `None` when this container
    /// may not mount.
    ///
    /// `None` rather than a panic is the honest answer to a missing capability:
    /// the caller states the blind spot in its own output and stops. It is not
    /// a skip of a case that could have run — a container without
    /// `CAP_SYS_ADMIN` cannot manufacture a small filesystem by any other
    /// means.
    fn mount(at: &Path, bytes: u64) -> Option<Self> {
        create_dir_all(at).expect("the scratch root must be creatable");
        std::fs::set_permissions(at, Permissions::from_mode(0o700))
            .expect("the scratch root must be root-only");

        let mounted = Command::new("mount")
            .args(["-t", "tmpfs", "-o"])
            .arg(format!("size={bytes},mode=0700"))
            .arg("maran-scratch-ceiling")
            .arg(at)
            .status()
            .expect("the polygon image installs mount");

        mounted.success().then(|| Self {
            at: at.to_path_buf(),
        })
    }
}

impl Drop for SmallScratch {
    fn drop(&mut self) {
        let _ = Command::new("umount").arg(&self.at).status();
    }
}

/// Fills `database` with about two megabytes, so its dump is large enough to
/// measure against a filesystem.
///
/// Doubling rather than a loop of inserts: eleven statements instead of two
/// thousand, and the table's contents are irrelevant — only its size is.
fn give_the_database_bulk(server: &PolygonMariadb, database: &DatabaseName) {
    let name = database.as_str();
    let mut statements = format!(
        "CREATE TABLE `{name}`.`bulk` (padding LONGTEXT); \
         INSERT INTO `{name}`.`bulk` VALUES (REPEAT('m', 1024));"
    );
    for _ in 0..11 {
        statements.push_str(&format!(
            "INSERT INTO `{name}`.`bulk` SELECT * FROM `{name}`.`bulk`;"
        ));
    }

    server.run(&statements);
}

#[test]
#[ignore = "mounts a real filesystem and dumps a real database: polygon only"]
fn a_dump_is_refused_at_nine_tenths_of_what_the_scratch_filesystem_actually_holds() {
    require_polygon();

    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polybackseven");
    clear_backups(account.name());
    let database = give_account_a_database(&server, account.name());
    give_the_database_bulk(&server, &database);
    write(account.home().join("index.html"), HOME_FILE_CONTENTS).expect("a file in the home");

    // How big this database's dump really is, taken with the same host the
    // operation uses, so the filesystem below is sized against a measurement
    // and never against a guess about `mysqldump`'s output.
    let host = ProcessBackupHost::new(polygon_distro());
    let probe = PathBuf::from("/tmp/maran-scratch-ceiling-probe");
    let _ = std::fs::remove_dir_all(&probe);
    create_dir_all(&probe).expect("the probe directory");
    let dump_bytes = host
        .dump_database(&database, &probe.join("probe.sql"))
        .unwrap_or_else(|error| panic!("the fixture database must be dumpable: {error}"));
    let _ = std::fs::remove_dir_all(&probe);
    assert!(
        dump_bytes >= SMALLEST_USABLE_DUMP,
        "the fixture dump is only {dump_bytes} bytes, too small to size a filesystem against"
    );

    let scratch_root = PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT);
    let Some(_scratch) =
        SmallScratch::mount(&scratch_root, dump_bytes * SCRATCH_SIZE_PER_MILLE / 1000)
    else {
        eprintln!(
            "UNOBSERVED HERE: this container may not mount, so the create side's per-dump ceiling \
             was not exercised. It needs `docker run --privileged`; see docker/README.md."
        );
        return;
    };

    // The precondition, observed on the mounted filesystem rather than assumed
    // from the size that was asked for: the dump must FIT (or the refusal would
    // be `ENOSPC`, which both the code and its mutant produce) and must exceed
    // nine tenths of the room (or there would be no refusal to read).
    let available = df_available_bytes(&scratch_root);
    assert!(
        available > dump_bytes,
        "the scratch holds {available} bytes and the dump needs {dump_bytes}: the dump would fail \
         on ENOSPC before any ceiling was consulted"
    );
    assert!(
        available / SPENDABLE_DENOMINATOR * SPENDABLE_NUMERATOR < dump_bytes,
        "the scratch holds {available} bytes, nine tenths of which already fits a {dump_bytes} \
         byte dump: there is no ceiling to observe here"
    );

    let refusal = create_backup(
        &host,
        &catalog(),
        account.name(),
        &fixture_id(),
        &root(),
        &mut RecordingSink::default(),
    )
    .expect_err("a dump larger than the scratch's share must be refused");

    let BackupError::DumpTooLarge { limit, actual } = refusal else {
        panic!("the refusal must name the ceiling it applied, got: {refusal}");
    };

    // The assertion this case exists for, and it is on the VALUE. The limit is
    // nine tenths of what THIS filesystem reports, which is a different number
    // on every host and in every container — the one thing a per-run constant
    // can never be.
    let expected = available / SPENDABLE_DENOMINATOR * SPENDABLE_NUMERATOR;
    let difference = limit.abs_diff(expected) as f64 / expected as f64;
    assert!(
        difference <= AVAILABLE_TOLERANCE,
        "the ceiling applied was {limit}; nine tenths of the {available} bytes df reports on the \
         scratch is {expected}, off by {:.2}%. A limit of {CONSTANT_DUMP_CEILING} is the signature \
         of the per-database constant reaching the loop unnarrowed, which is the same ceiling for \
         every dump and for every host",
        difference * 100.0
    );
    assert_eq!(
        actual, dump_bytes,
        "the refusal must report the size of the dump that was actually taken"
    );

    // Nothing survives the refusal: no artifact, and no copy of the customer's
    // database left behind on the scratch.
    assert!(!artifact_of(account.name(), &fixture_id()).exists());
    assert!(
        !AgentPaths::backup_scratch_dir(&fixture_id()).exists(),
        "the scratch holding the dump outlived the refusal"
    );
}
