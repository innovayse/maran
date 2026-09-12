//! The startup reconciliation: what it finishes, what it leaves, what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir_all, read_to_string, write};
use std::os::unix::fs::symlink;
use std::path::{Path, PathBuf};

use tempfile::TempDir;

use maran_agent_core::validation::system::backup_id::BackupId;

use crate::backup::model::restore_marker::RESTORE_MARKER_VERSION;
use crate::backup::restore_marker_file::restore_marker_path;

use super::*;

// Every case names its OWN account, and that is not cosmetic: `take_account_lock`
// is a process-wide registry, the reconciliation takes it per account, and the
// harness runs these cases on parallel threads. Two cases sharing a name make one
// of them skip its swap with `refused` — which is the lock doing exactly its job,
// measured here as a pair of failures that only appeared in the full run.

/// The backup id every case reconciles.
const BACKUP_ID: &str = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

/// The bytes that make "the right home came back" an assertion about a VALUE.
const RESTORED_BYTES: &str = "the bytes the backup held\n";

/// The bytes the account had before the restore started.
const LIVE_BYTES: &str = "the customer's own bytes\n";

/// A host's three roots, laid out where a test may really create them.
struct Host {
    /// The tree, removed when the fixture drops.
    root: TempDir,
    /// This case's own account name.
    account: String,
}

impl Host {
    /// Creates the staging root, the home root and the scratch root for a case
    /// that owns `account` alone.
    fn new(account: &str) -> Self {
        let host = Self {
            root: TempDir::new().expect("a temporary directory"),
            account: account.to_owned(),
        };
        create_dir_all(host.staging_root()).expect("the staging root");
        create_dir_all(host.home_root()).expect("the home root");
        create_dir_all(host.scratch_root()).expect("the scratch root");
        // The modes the installer really gives these, applied explicitly rather
        // than inherited from this process's umask: the reconciliation refuses a
        // root anybody else may write into, and a developer whose umask is 0002
        // would otherwise be testing that refusal in every case.
        set_mode(&host.staging_root(), 0o711);
        set_mode(&host.scratch_root(), 0o700);
        host
    }

    /// `AgentPaths::RESTORE_STAGING_ROOT`'s stand-in.
    fn staging_root(&self) -> PathBuf {
        self.root.path().join("restore-staging")
    }

    /// `AgentPaths::ACCOUNT_HOME_ROOT`'s stand-in.
    fn home_root(&self) -> PathBuf {
        self.root.path().join("home")
    }

    /// `AgentPaths::BULK_SCRATCH_ROOT`'s stand-in.
    fn scratch_root(&self) -> PathBuf {
        self.root.path().join("scratch")
    }

    /// This case's account, validated.
    fn account(&self) -> AccountName {
        AccountName::parse(&self.account).expect("the fixture's name is valid")
    }

    /// The account's home.
    fn home(&self) -> PathBuf {
        self.home_root().join(&self.account)
    }

    /// The parked previous home, named exactly as `AgentPaths` names it.
    fn previous(&self) -> PathBuf {
        self.staging_root().join(
            AgentPaths::restore_previous_dir(&self.account(), &backup_id())
                .file_name()
                .expect("a composed path has a final component"),
        )
    }

    /// The staging tree, named exactly as `AgentPaths` names it.
    fn staging(&self) -> PathBuf {
        self.staging_root().join(
            AgentPaths::restore_staging_dir(&self.account(), &backup_id())
                .file_name()
                .expect("a composed path has a final component"),
        )
    }

    /// The marker's path.
    fn marker(&self) -> PathBuf {
        self.staging_root().join(
            restore_marker_path(&self.account(), &backup_id())
                .file_name()
                .expect("a composed path has a final component"),
        )
    }

    /// Writes the marker a restore would have written before its first rename.
    fn mark(&self) -> &Self {
        let marker = RestoreMarker {
            version: RESTORE_MARKER_VERSION,
            account: self.account.clone(),
            backup_id: BACKUP_ID.to_owned(),
            owner_uid: own_uid(),
            home_gid: own_gid(),
            home_mode: 0o750,
        };
        write(
            self.marker(),
            serde_json::to_vec(&marker).expect("the document encodes"),
        )
        .expect("the marker");
        self
    }

    /// Creates a directory holding one named file with the given contents.
    fn tree(&self, path: &Path, contents: &str) -> &Self {
        create_dir_all(path).expect("a tree of the fixture");
        write(path.join("index.html"), contents).expect("the tree's file");
        self
    }

    /// Reconciles, as the daemon would at startup.
    fn reconcile(&self) -> RestoreRecovery {
        recover_in(
            &self.staging_root(),
            &self.home_root(),
            &self.scratch_root(),
            own_uid(),
        )
    }

    /// What the account's home holds now.
    fn home_contents(&self) -> String {
        read_to_string(self.home().join("index.html")).expect("the home has its file")
    }
}

/// Sets `path`'s permission bits.
fn set_mode(path: &Path, mode: u32) {
    std::fs::set_permissions(path, std::os::unix::fs::PermissionsExt::from_mode(mode))
        .expect("the fixture owns its own directories");
}

/// The backup, validated.
fn backup_id() -> BackupId {
    BackupId::parse(BACKUP_ID).expect("the fixture's id is a uuid")
}

/// This process's own uid — the owner the injected roots really have.
fn own_uid() -> u32 {
    rustix::process::getuid().as_raw()
}

/// This process's own gid, so `reown` has a group it may actually apply.
fn own_gid() -> u32 {
    rustix::process::getgid().as_raw()
}

#[test]
fn a_swap_killed_between_the_two_renames_is_finished_forward_and_the_home_comes_back() {
    let host = Host::new("recovone");
    host.mark();
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);

    // The state F-2 is about, asserted before the recovery so the case cannot
    // pass by never having been in it.
    assert!(!host.home().exists(), "the window has no home");

    let recovery = host.reconcile();

    assert_eq!(recovery.swaps_completed, 1);
    assert_eq!(recovery.swaps_rolled_back, 0);
    // The restored bytes and not the live ones: forward, not back. A rollback
    // would leave the customer the old home beside their new databases.
    assert_eq!(host.home_contents(), RESTORED_BYTES);
    assert!(!host.previous().exists(), "the parked tree is reclaimed");
    assert!(!host.marker().exists(), "the marker is spent");
}

#[test]
fn a_finished_restore_is_left_exactly_as_it_is() {
    let host = Host::new("recovtwo");
    host.mark();
    host.tree(&host.home(), RESTORED_BYTES);

    let recovery = host.reconcile();

    // The value that says it did nothing. Without it "the home is still there"
    // would pass just as well against a reconciler that moved it away and back.
    assert_eq!(recovery.swaps_already_done, 1);
    assert_eq!(recovery.swaps_completed, 0);
    assert_eq!(recovery.swaps_rolled_back, 0);
    assert_eq!(recovery.swaps_abandoned, 0);
    assert_eq!(recovery.refused, 0);
    assert_eq!(host.home_contents(), RESTORED_BYTES);
    assert!(!host.marker().exists());
}

#[test]
fn a_swap_killed_before_its_first_rename_leaves_the_live_home_untouched() {
    let host = Host::new("recovthree");
    host.mark();
    host.tree(&host.home(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);

    let recovery = host.reconcile();

    assert_eq!(recovery.swaps_abandoned, 1);
    assert_eq!(recovery.swaps_completed, 0);
    assert_eq!(host.home_contents(), LIVE_BYTES);
    assert!(!host.staging().exists());
}

#[test]
fn a_swap_killed_after_its_second_rename_is_finished_rather_than_reversed() {
    let host = Host::new("recovfour");
    host.mark();
    host.tree(&host.home(), RESTORED_BYTES);
    host.tree(&host.previous(), LIVE_BYTES);

    let recovery = host.reconcile();

    assert_eq!(recovery.swaps_completed, 1);
    assert_eq!(host.home_contents(), RESTORED_BYTES);
    assert!(!host.previous().exists());
}

#[test]
fn a_parked_home_with_no_replacement_left_is_put_back() {
    let host = Host::new("recovfive");
    host.mark();
    host.tree(&host.previous(), LIVE_BYTES);

    let recovery = host.reconcile();

    assert_eq!(recovery.swaps_rolled_back, 1);
    assert_eq!(recovery.swaps_completed, 0);
    assert_eq!(host.home_contents(), LIVE_BYTES);
}

#[test]
fn a_live_home_beside_a_parked_one_is_refused_and_the_parked_copy_is_kept() {
    let host = Host::new("recovsix");
    host.mark();
    host.tree(&host.home(), LIVE_BYTES);
    host.tree(&host.previous(), "an older home\n");
    host.tree(&host.staging(), RESTORED_BYTES);

    let recovery = host.reconcile();

    assert_eq!(recovery.refused, 1);
    assert_eq!(recovery.swaps_completed, 0);
    // The live home is not overwritten and the parked one is not thrown away:
    // only a human can say which of two homes an account is meant to have.
    assert_eq!(host.home_contents(), LIVE_BYTES);
    assert!(host.previous().exists());
    assert!(!host.staging().exists(), "the staging tree is litter");
}

#[test]
fn a_symbolic_link_where_the_home_belongs_stops_the_reconciliation_of_that_swap() {
    let host = Host::new("recovseven");
    host.mark();
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);
    let elsewhere = host.root.path().join("elsewhere");
    create_dir_all(&elsewhere).expect("the target");
    symlink(&elsewhere, host.home()).expect("the link");

    let recovery = host.reconcile();

    assert_eq!(recovery.refused, 1);
    assert_eq!(recovery.swaps_completed, 0);
    // Nothing at all, including the marker: the next start must see this again.
    assert!(host.marker().exists());
    assert!(host.staging().exists());
    assert!(host.previous().exists());
    assert!(
        !elsewhere.join("index.html").exists(),
        "nothing may be written through the link"
    );
}

#[test]
fn a_marker_naming_an_account_that_would_not_validate_moves_nothing() {
    let host = Host::new("recoveight");
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);
    let hostile = RestoreMarker {
        version: RESTORE_MARKER_VERSION,
        account: "../../etc".to_owned(),
        backup_id: BACKUP_ID.to_owned(),
        owner_uid: own_uid(),
        home_gid: own_gid(),
        home_mode: 0o750,
    };
    write(
        host.marker(),
        serde_json::to_vec(&hostile).expect("it encodes"),
    )
    .expect("the marker");

    let recovery = host.reconcile();

    assert_eq!(recovery.refused, 1);
    assert_eq!(recovery.swaps_completed, 0);
    assert!(host.previous().exists());
}

#[test]
fn directories_in_the_staging_root_with_no_marker_are_left_for_an_operator() {
    let host = Host::new("recovnine");
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);

    let recovery = host.reconcile();

    // Without a marker the account is only INFERABLE from the name. Litter costs
    // disk; a rename into the wrong `/home/<account>` costs a customer's home.
    assert_eq!(recovery.unmarked_left, 2);
    assert_eq!(recovery.swaps_completed, 0);
    assert_eq!(recovery.swaps_rolled_back, 0);
    assert!(host.previous().exists());
    assert!(!host.home().exists());
}

#[test]
fn a_staging_root_that_anybody_may_write_into_is_refused_whole() {
    let host = Host::new("recovten");
    host.mark();
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);
    set_mode(&host.staging_root(), 0o777);

    let recovery = host.reconcile();

    // A world-writable staging root means a marker anybody could have planted,
    // and acting on one is a root rename into a home somebody else named.
    assert_eq!(recovery.swaps_completed, 0);
    assert!(recovery.refused >= 1);
    assert!(!host.home().exists());
}

#[test]
fn a_root_owned_by_somebody_else_is_refused_which_is_what_makes_the_check_observable() {
    let host = Host::new("recoveleven");
    host.mark();
    host.tree(&host.previous(), LIVE_BYTES);
    host.tree(&host.staging(), RESTORED_BYTES);

    // The inverse control for the accepting cases above: the same tree, with the
    // ONE thing changed that the ownership gate looks at. A gate only ever shown
    // input it accepts passes just as well once mutated into accepting
    // everything (rules/testing.md).
    let recovery = recover_in(
        &host.staging_root(),
        &host.home_root(),
        &host.scratch_root(),
        own_uid() + 1,
    );

    assert_eq!(recovery.swaps_completed, 0);
    assert!(recovery.refused >= 1);
    assert!(!host.home().exists());
}

#[test]
fn a_marker_whose_write_was_itself_interrupted_is_swept_and_acts_on_nothing() {
    let host = Host::new("recovtwelve");
    host.tree(&host.home(), LIVE_BYTES);
    let partial = host
        .marker()
        .with_extension(PARTIAL_MARKER_SUFFIX.trim_start_matches('.'));
    write(&partial, b"{\"version\":1,\"acc").expect("half a document");

    let recovery = host.reconcile();

    assert!(!partial.exists());
    assert_eq!(recovery.refused, 0);
    assert_eq!(recovery.unmarked_left, 0);
    assert_eq!(host.home_contents(), LIVE_BYTES);
}

#[test]
fn the_reconciliation_reaps_the_scratch_in_the_same_pass() {
    let host = Host::new("recovthirteen");
    let operation = host.scratch_root().join("backup").join(BACKUP_ID);
    create_dir_all(operation.join("databases")).expect("the extracted dumps");
    write(operation.join("databases/shop.sql"), "SELECT 1;\n").expect("an extracted dump");
    create_dir_all(operation.join("rollback")).expect("the rollback directory");
    write(operation.join("rollback/shop.sql"), "SELECT 'before';\n").expect("a rollback dump");

    let recovery = host.reconcile();

    assert_eq!(recovery.rollback_sets_kept, 1);
    assert_eq!(
        read_to_string(operation.join("rollback/shop.sql")).expect("the rollback dump survives"),
        "SELECT 'before';\n"
    );
    assert!(!operation.join("databases").exists());
}
