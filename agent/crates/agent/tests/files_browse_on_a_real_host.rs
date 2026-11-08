//! The browse rpcs doing their ordinary work against a real home (issue #166):
//! listing, sorting and paging, reading byte-exact across chunks, and name
//! search.
//!
//! The hostile cases live in `files_browse_containment_on_a_real_host.rs` and
//! the ceilings in `files_browse_bounds_on_a_real_host.rs`; this suite is the
//! one that proves the rpcs are useful at all, against the kernel's own answers
//! (sizes, modes, times, device numbers) rather than a fake directory.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/browse_session.rs"]
mod browse_session;
#[path = "fixtures/held_read.rs"]
mod held_read;
#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_home_tree.rs"]
mod polygon_home_tree;
#[path = "fixtures/read_outcome.rs"]
mod read_outcome;
#[path = "fixtures/tmpfs_mount.rs"]
mod tmpfs_mount;

use maran_agent::proto::{DirectoryEntry, DirectorySort, ErrorCode};

use browse_session::BrowseSession;
use polygon_account::PolygonAccount;
use polygon_home_tree::PolygonHomeTree;
use tmpfs_mount::TmpfsMount;

/// A ceiling no file in these tests comes near.
const ROOMY: u64 = 4 * 1024 * 1024;

/// Names of the entries, in the order they were returned.
fn names(entries: &[DirectoryEntry]) -> Vec<&str> {
    entries.iter().map(|entry| entry.name.as_str()).collect()
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn the_home_and_a_nested_directory_list_with_the_kernels_own_figures() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrlisting");
    let tree = PolygonHomeTree::new(&account);
    let file = tree.file("docs/readme.txt", b"hello, twelve!");
    std::fs::set_permissions(&file, std::os::unix::fs::PermissionsExt::from_mode(0o640)).unwrap();
    tree.set_modified(&file, 1_700_000_000);
    tree.directory("docs/empty");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let home = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .unwrap();
    assert_eq!(names(&home.entries), ["docs"]);
    assert!(home.entries[0].is_directory);

    let docs = session
        .list(BrowseSession::list_request(name, "docs"))
        .await
        .unwrap();
    assert_eq!(
        names(&docs.entries),
        ["empty", "readme.txt"],
        "directories first"
    );
    let readme = &docs.entries[1];
    assert_eq!(readme.size_bytes, 14);
    assert_eq!(readme.mode & 0o777, 0o640);
    assert_eq!(readme.modified_at_unix, 1_700_000_000);
    assert_eq!(readme.uid, account.ids().uid());
    assert_eq!(readme.gid, account.ids().gid());
    assert_eq!(readme.link_count, 1);
    assert!(!readme.is_directory && !readme.is_symlink);

    let empty = session
        .list(BrowseSession::list_request(name, "docs/empty"))
        .await
        .unwrap();
    assert!(empty.entries.is_empty());
    assert_eq!(empty.total_entries, 0);

    // A file is not a directory, and an absent directory is a level the walk could not
    // open: VALIDATION_FAILED, as `files.proto` states (a missing FILE is NOT_FOUND, below).
    let not_a_directory = session
        .list(BrowseSession::list_request(name, "docs/readme.txt"))
        .await
        .expect_err("a file does not list");
    BrowseSession::assert_refused(&not_a_directory);
    let missing = session
        .list(BrowseSession::list_request(name, "docs/absent"))
        .await
        .expect_err("an absent directory");
    BrowseSession::assert_code(&missing, ErrorCode::ValidationFailed);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn hidden_entries_are_listed_only_when_asked_for() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrhidden");
    let tree = PolygonHomeTree::new(&account);
    tree.file(".secret-dotfile", b"x");
    tree.file("visible.txt", b"x");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let default = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .unwrap();
    assert_eq!(names(&default.entries), ["visible.txt"]);
    assert_eq!(
        default.total_entries, 1,
        "the total is after the hidden filter"
    );

    let mut with_hidden = BrowseSession::list_request(name, "");
    with_hidden.include_hidden = true;
    let all = session.list(with_hidden).await.unwrap();
    assert_eq!(names(&all.entries), [".secret-dotfile", "visible.txt"]);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn entries_sort_by_name_size_and_time_in_both_directions_with_directories_first() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrsorting");
    let tree = PolygonHomeTree::new(&account);
    let big = tree.file("b-big", &[0_u8; 300]);
    let small = tree.file("c-small", &[0_u8; 10]);
    let middle = tree.file("a-middle", &[0_u8; 100]);
    tree.directory("z-directory");
    tree.set_modified(&big, 3_000);
    tree.set_modified(&small, 1_000);
    tree.set_modified(&middle, 2_000);
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let cases = [
        (
            DirectorySort::Name,
            false,
            ["z-directory", "a-middle", "b-big", "c-small"],
        ),
        (
            DirectorySort::Name,
            true,
            ["z-directory", "c-small", "b-big", "a-middle"],
        ),
        (
            DirectorySort::Size,
            false,
            ["z-directory", "c-small", "a-middle", "b-big"],
        ),
        (
            DirectorySort::Size,
            true,
            ["z-directory", "b-big", "a-middle", "c-small"],
        ),
        (
            DirectorySort::Modified,
            false,
            ["z-directory", "c-small", "a-middle", "b-big"],
        ),
        (
            DirectorySort::Modified,
            true,
            ["z-directory", "b-big", "a-middle", "c-small"],
        ),
    ];
    for (sort, descending, expected) in cases {
        let mut request = BrowseSession::list_request(name, "");
        request.sort = sort as i32;
        request.descending = descending;
        let listed = session.list(request).await.unwrap();
        assert_eq!(
            names(&listed.entries),
            expected,
            "{sort:?} descending={descending}"
        );
    }
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn paging_returns_the_requested_window_and_the_total_before_paging() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrpaging");
    let tree = PolygonHomeTree::new(&account);
    for index in 0..25 {
        tree.file(&format!("file-{index:02}"), b"x");
    }
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let mut request = BrowseSession::list_request(name, "");
    request.offset = 10;
    request.limit = 5;
    let page = session.list(request.clone()).await.unwrap();
    assert_eq!(
        names(&page.entries),
        ["file-10", "file-11", "file-12", "file-13", "file-14"]
    );
    assert_eq!(page.total_entries, 25, "the total is before paging");

    request.offset = 20;
    let last = session.list(request.clone()).await.unwrap();
    assert_eq!(names(&last.entries).len(), 5);

    request.offset = 25;
    let past = session.list(request).await.unwrap();
    assert!(
        past.entries.is_empty(),
        "past the end is an empty page, not an error"
    );
    assert_eq!(past.total_entries, 25);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn names_that_cannot_be_sent_are_skipped_and_counted() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrbadnames");
    let tree = PolygonHomeTree::new(&account);
    let home = account.home().to_path_buf();
    tree.file_named_by_bytes(&home, b"not-utf8-\xff\xfe");
    tree.file_named_by_bytes(&home, b"line\nbreak");
    tree.file("fine.txt", b"x");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let listed = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .unwrap();
    // Control: the ordinary name is there, so the skip is not "list nothing".
    assert_eq!(names(&listed.entries), ["fine.txt"]);
    assert_eq!(
        listed.skipped_entries, 2,
        "an entry the panel cannot name is counted, not silently invisible"
    );
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_file_reads_byte_exact_across_many_chunks() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrchunks");
    let tree = PolygonHomeTree::new(&account);
    // A pattern with a prime period, so a lost, repeated or reordered chunk
    // cannot reproduce it.
    let content: Vec<u8> = (0..300_000_u32).map(|index| (index % 251) as u8).collect();
    tree.file("pattern.bin", &content);
    tree.file("empty.bin", b"");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let read = session
        .read(BrowseSession::read_request(name, "pattern.bin", ROOMY))
        .await;
    assert!(read.error.is_none());
    assert!(read.chunks > 1, "300 kB must arrive in more than one chunk");
    assert_eq!(read.bytes, content);

    let empty = session
        .read(BrowseSession::read_request(name, "empty.bin", ROOMY))
        .await;
    assert!(empty.error.is_none());
    assert!(empty.bytes.is_empty());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_read_refuses_a_directory_and_reports_a_missing_file_as_not_found() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrreadkinds");
    let tree = PolygonHomeTree::new(&account);
    tree.directory("a-directory");
    tree.file("present.txt", b"here");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let directory = session
        .read(BrowseSession::read_request(name, "a-directory", ROOMY))
        .await;
    BrowseSession::assert_refused(&directory.error.expect("a directory is not a file"));

    let missing = session
        .read(BrowseSession::read_request(name, "absent.txt", ROOMY))
        .await;
    BrowseSession::assert_code(&missing.error.expect("an absent file"), ErrorCode::NotFound);

    let present = session
        .read(BrowseSession::read_request(name, "present.txt", ROOMY))
        .await;
    assert_eq!(present.bytes, b"here");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn search_finds_a_nested_match_case_insensitively_and_honours_hidden() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrsearch");
    let tree = PolygonHomeTree::new(&account);
    tree.file("a/b/c/Report-FINAL.txt", b"x");
    tree.file("a/.hidden-report", b"x");
    tree.file("unrelated.txt", b"x");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let found = session
        .search(BrowseSession::search_request(name, "", "report"))
        .await
        .unwrap();
    let paths: Vec<_> = found.matches.iter().map(|m| m.path.as_str()).collect();
    assert_eq!(
        paths,
        ["a/b/c/Report-FINAL.txt"],
        "case-insensitive, nested, hidden left out"
    );
    assert!(!found.truncated);

    let mut with_hidden = BrowseSession::search_request(name, "", "report");
    with_hidden.include_hidden = true;
    let found = session.search(with_hidden).await.unwrap();
    let mut paths: Vec<_> = found.matches.iter().map(|m| m.path.clone()).collect();
    paths.sort();
    assert_eq!(paths, ["a/.hidden-report", "a/b/c/Report-FINAL.txt"]);

    // The match carries the entry's own figures, as a listing would.
    let entry = found.matches[0].entry.as_ref().unwrap();
    assert!(!entry.name.is_empty());

    // A query with a slash is not a name.
    let refused = session
        .search(BrowseSession::search_request(name, "", "a/b"))
        .await
        .expect_err("a query is a name, not a path");
    BrowseSession::assert_code(&refused, ErrorCode::InvalidInput);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "mounts a real filesystem inside a home: privileged polygon only"]
async fn search_does_not_cross_into_another_filesystem_but_does_when_started_inside_it() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrxdev");
    let tree = PolygonHomeTree::new(&account);
    tree.file("on-disk/needle-disk.txt", b"x");
    let mount_point = tree.directory("mounted");
    // Declared after the account, so it is dropped first: the account's removal
    // must not walk into a live mount.
    let Some(_mount) = TmpfsMount::mount(&mount_point) else {
        println!(
            "UNOBSERVED HERE: this container may not mount, so the search's refusal to cross \
             onto another filesystem was not measured"
        );
        return;
    };
    tree.give_to_account(&mount_point);
    tree.file("mounted/needle-tmpfs.txt", b"x");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let from_home = session
        .search(BrowseSession::search_request(name, "", "needle"))
        .await
        .unwrap();
    let paths: Vec<_> = from_home.matches.iter().map(|m| m.path.as_str()).collect();
    // Control: the same filesystem is searched.
    assert_eq!(
        paths,
        ["on-disk/needle-disk.txt"],
        "nothing below the mount"
    );

    // Control, inverse: started on the other device, its own files are found.
    let from_mount = session
        .search(BrowseSession::search_request(name, "mounted", "needle"))
        .await
        .unwrap();
    let paths: Vec<_> = from_mount.matches.iter().map(|m| m.path.as_str()).collect();
    assert_eq!(paths, ["mounted/needle-tmpfs.txt"]);
}
