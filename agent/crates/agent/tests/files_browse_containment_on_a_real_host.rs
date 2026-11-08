//! What a customer can plant in their own home to make the root agent read
//! outside it, and the proof that the browse rpcs refuse each one (issue #166).
//!
//! Every attack here is built as root with ordinary file calls and then put to
//! the real agent over its real socket: traversal, a link in the middle of a
//! path, a link at the end of it, a link into another account's home, a file
//! owned by somebody else, a second name for a file the account does not hold
//! alone, and entries that block or misbehave when opened (FIFO, device,
//! socket). The containment is the descriptor walk inside the forked child;
//! the parse of the path is advice, so the symlink cases are the ones that
//! matter and the `..` case is only the boundary.
//!
//! **Every refusal has a positive control in the same test**: the legitimate
//! thing next to the attack is asked for too and must succeed. A probe that
//! refuses everything — a walk mutated to fail closed on all input — passes
//! every test that only hands it hostile input (rules/testing.md).
//!
//! The ownership and link-count checks are each isolated by their own test: a
//! hard link to a root-owned file fails BOTH checks, so only the cases with
//! one failing condition each can tell which check is holding.

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

use maran_agent::proto::ErrorCode;

use browse_session::BrowseSession;
use polygon_account::PolygonAccount;
use polygon_home_tree::PolygonHomeTree;

/// A ceiling no file in these tests comes near.
const ROOMY: u64 = 1024 * 1024;

/// Names of the entries a listing returned.
fn names(entries: &[maran_agent::proto::DirectoryEntry]) -> Vec<String> {
    entries.iter().map(|entry| entry.name.clone()).collect()
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn dot_dot_and_absolute_paths_are_refused_by_every_browse_rpc() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrdotdot");
    let tree = PolygonHomeTree::new(&account);
    tree.file("inside/notes.txt", b"mine");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    for hostile in [
        "../",
        "inside/../..",
        "/etc",
        "inside/../../pbrother",
        "./inside",
        "inside//x",
    ] {
        let listed = session
            .list(BrowseSession::list_request(name, hostile))
            .await
            .expect_err(hostile);
        BrowseSession::assert_code(&listed, ErrorCode::InvalidInput);

        let read = session
            .read(BrowseSession::read_request(name, hostile, ROOMY))
            .await;
        BrowseSession::assert_code(
            &read
                .error
                .unwrap_or_else(|| panic!("read of {hostile} must be refused")),
            ErrorCode::InvalidInput,
        );
        assert!(read.bytes.is_empty(), "no byte may leave on a refused path");

        let searched = session
            .search(BrowseSession::search_request(name, hostile, "notes"))
            .await
            .expect_err(hostile);
        BrowseSession::assert_code(&searched, ErrorCode::InvalidInput);
    }

    // Control: the same three rpcs answer for the legitimate path.
    let listed = session
        .list(BrowseSession::list_request(name, "inside"))
        .await
        .unwrap();
    assert_eq!(names(&listed.entries), ["notes.txt"]);
    let read = session
        .read(BrowseSession::read_request(name, "inside/notes.txt", ROOMY))
        .await;
    assert_eq!(read.bytes, b"mine");
    let found = session
        .search(BrowseSession::search_request(name, "", "notes"))
        .await
        .unwrap();
    assert_eq!(found.matches.len(), 1);
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_symlinked_component_is_refused_by_the_walk_and_a_real_one_is_not() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrcomponent");
    let tree = PolygonHomeTree::new(&account);
    tree.symlink("escape", "/etc");
    tree.symlink("nested-escape-parent", ".");
    tree.file("real/passwd", b"the account's own file");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // The link is a COMPONENT of the path: listing it, reading through it and
    // searching from it must all be refused, because the walk opens every level
    // with O_NOFOLLOW from the descriptor above.
    let listed = session
        .list(BrowseSession::list_request(name, "escape"))
        .await
        .expect_err("escape -> /etc must not list");
    BrowseSession::assert_refused(&listed);

    let read = session
        .read(BrowseSession::read_request(name, "escape/passwd", ROOMY))
        .await;
    BrowseSession::assert_refused(&read.error.expect("a read through a link must be refused"));
    assert!(
        read.bytes.is_empty(),
        "not one byte of /etc/passwd may leave through a symlinked component"
    );

    let searched = session
        .search(BrowseSession::search_request(name, "escape", "passwd"))
        .await
        .expect_err("a search may not start inside a link");
    BrowseSession::assert_refused(&searched);

    // A link to the directory it sits in is no exception: a link is refused
    // because it is a link, not because of where it points.
    let loop_listed = session
        .list(BrowseSession::list_request(name, "nested-escape-parent"))
        .await
        .expect_err("a link to '.' must not list");
    BrowseSession::assert_refused(&loop_listed);

    // Control: the real directory beside it lists and its file reads byte-exact.
    let real = session
        .list(BrowseSession::list_request(name, "real"))
        .await
        .unwrap();
    assert_eq!(names(&real.entries), ["passwd"]);
    let bytes = session
        .read(BrowseSession::read_request(name, "real/passwd", ROOMY))
        .await;
    assert_eq!(bytes.bytes, b"the account's own file");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_directory_swapped_for_a_link_between_two_requests_is_refused_on_the_second() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrswap");
    let tree = PolygonHomeTree::new(&account);
    let swapped = tree
        .file("swap/secret.txt", b"fine")
        .parent()
        .unwrap()
        .to_path_buf();
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // First walk: the real directory. Nothing is cached between requests.
    let before = session
        .list(BrowseSession::list_request(name, "swap"))
        .await
        .unwrap();
    assert_eq!(names(&before.entries), ["secret.txt"]);

    std::fs::remove_dir_all(&swapped).unwrap();
    tree.symlink("swap", "/etc");

    let after = session
        .list(BrowseSession::list_request(name, "swap"))
        .await
        .expect_err("the directory became a link to /etc");
    BrowseSession::assert_refused(&after);
    let read = session
        .read(BrowseSession::read_request(name, "swap/passwd", ROOMY))
        .await;
    BrowseSession::assert_refused(&read.error.expect("the read must be refused too"));
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_symlink_lists_as_a_link_with_its_own_size_and_cannot_be_read() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrleaf");
    let tree = PolygonHomeTree::new(&account);
    let target = "/etc/passwd";
    tree.symlink("passwd-link", target);
    tree.symlink("etc-link", "/etc");
    tree.file("plain.txt", b"read me");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();
    let real_size = std::fs::metadata(target).unwrap().len();
    assert_ne!(
        real_size,
        target.len() as u64,
        "the probe needs a target whose size differs from its link's length"
    );

    let listed = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .unwrap();
    let link = listed
        .entries
        .iter()
        .find(|entry| entry.name == "passwd-link")
        .expect("the link is listed");
    assert!(link.is_symlink, "a link lists as a link");
    assert!(!link.is_directory);
    assert_eq!(
        link.size_bytes,
        target.len() as u64,
        "the size is the link's own length, never the target's"
    );
    let directory_link = listed
        .entries
        .iter()
        .find(|entry| entry.name == "etc-link")
        .expect("the directory link is listed");
    assert!(
        directory_link.is_symlink && !directory_link.is_directory,
        "a link to a directory is a link, not a directory"
    );

    let read = session
        .read(BrowseSession::read_request(name, "passwd-link", ROOMY))
        .await;
    BrowseSession::assert_refused(&read.error.expect("a symlink leaf must not be read"));
    assert!(read.bytes.is_empty(), "no byte of /etc/passwd may leave");

    // Control: the regular file next to it reads byte-exact.
    let plain = session
        .read(BrowseSession::read_request(name, "plain.txt", ROOMY))
        .await;
    assert_eq!(plain.bytes, b"read me");
    assert!(plain.error.is_none());
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates real system accounts and drops privileges: polygon only"]
async fn a_link_into_another_accounts_home_is_refused() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrthief");
    let victim = PolygonAccount::create("pbrvictim");
    PolygonHomeTree::new(&victim).file("secret.txt", b"the victim's secret");
    let tree = PolygonHomeTree::new(&account);
    tree.symlink("next-door", victim.home().to_str().unwrap());
    tree.symlink(
        "next-door-file",
        victim.home().join("secret.txt").to_str().unwrap(),
    );
    tree.file("mine/ok.txt", b"ok");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let listed = session
        .list(BrowseSession::list_request(name, "next-door"))
        .await
        .expect_err("the victim's home must not list through a link");
    BrowseSession::assert_refused(&listed);
    for path in ["next-door/secret.txt", "next-door-file"] {
        let read = session
            .read(BrowseSession::read_request(name, path, ROOMY))
            .await;
        BrowseSession::assert_refused(
            &read
                .error
                .unwrap_or_else(|| panic!("{path} must be refused")),
        );
        assert!(
            read.bytes.is_empty(),
            "the victim's bytes must not leave through {path}"
        );
    }

    // Control: the account's own file reads.
    let own = session
        .read(BrowseSession::read_request(name, "mine/ok.txt", ROOMY))
        .await;
    assert_eq!(own.bytes, b"ok");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates real system accounts and drops privileges: polygon only"]
async fn a_file_or_directory_owned_by_another_user_is_refused_inside_the_home() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrtenant");
    let other = PolygonAccount::create("pbrstranger");
    let tree = PolygonHomeTree::new(&account);

    // One failing condition each: a single-link regular file, a real directory,
    // no symlink anywhere — only the owner differs.
    let foreign_file = tree.file("foreign.txt", b"not yours");
    tree.give_to(&foreign_file, other.ids().uid(), other.ids().gid());
    let foreign_directory = tree.file("foreign-dir/inner.txt", b"inner");
    let foreign_directory = foreign_directory.parent().unwrap().to_path_buf();
    tree.give_to(&foreign_directory, other.ids().uid(), other.ids().gid());
    tree.file("own.txt", b"yours");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let read = session
        .read(BrowseSession::read_request(name, "foreign.txt", ROOMY))
        .await;
    BrowseSession::assert_refused(
        &read
            .error
            .expect("a file the account does not own is refused"),
    );
    assert!(read.bytes.is_empty());

    // Ownership is required at EVERY level of the walk, not only the leaf.
    let listed = session
        .list(BrowseSession::list_request(name, "foreign-dir"))
        .await
        .expect_err("a directory the account does not own must not list");
    BrowseSession::assert_refused(&listed);

    // Control: the account's own file in the same directory reads.
    let own = session
        .read(BrowseSession::read_request(name, "own.txt", ROOMY))
        .await;
    assert_eq!(own.bytes, b"yours");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_hardlink_to_a_root_owned_file_is_refused_for_read() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrhardroot");
    let tree = PolygonHomeTree::new(&account);
    // On the same filesystem as the home, which `/etc/hostname` in a container
    // is not (it is a bind mount): a root-owned file next to the homes.
    let secret = std::path::Path::new("/home").join("pbrhardroot-secret");
    std::fs::write(&secret, b"root's secret").unwrap();
    tree.hard_link("stolen", &secret);
    tree.file("own.txt", b"yours");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let read = session
        .read(BrowseSession::read_request(name, "stolen", ROOMY))
        .await;
    let _ = std::fs::remove_file(&secret);
    BrowseSession::assert_refused(
        &read
            .error
            .expect("a hard link to a root-owned file is refused"),
    );
    assert!(read.bytes.is_empty(), "root's bytes must not leave");

    let own = session
        .read(BrowseSession::read_request(name, "own.txt", ROOMY))
        .await;
    assert_eq!(own.bytes, b"yours");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_hardlink_to_a_file_the_account_owns_is_refused_by_its_link_count_alone() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrhardown");
    let tree = PolygonHomeTree::new(&account);

    // The inode belongs to the account and is a regular file: ONLY the link
    // count is wrong, because the second name sits outside the home. This is
    // the case that tells the link-count check from the owner check; the root
    // case above fails both.
    let outside = std::path::Path::new("/home").join("pbrhardown-outside");
    std::fs::write(&outside, b"owned, two names").unwrap();
    tree.give_to_account(&outside);
    tree.hard_link("second-name", &outside);
    // And two names inside the home: the customer's own multi-name file is
    // refused too, the documented cost of the rule (matches DeleteEntry).
    let inner = tree.file("first-inside", b"twice inside");
    tree.hard_link("second-inside", &inner);
    tree.file("single.txt", b"one name");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    for path in ["second-name", "first-inside", "second-inside"] {
        let read = session
            .read(BrowseSession::read_request(name, path, ROOMY))
            .await;
        BrowseSession::assert_refused(
            &read.error.unwrap_or_else(|| panic!("{path} has two links")),
        );
        assert!(read.bytes.is_empty(), "{path} must not leave a byte");
    }
    let _ = std::fs::remove_file(&outside);

    // Control: a file with one link, owned by the account, reads.
    let single = session
        .read(BrowseSession::read_request(name, "single.txt", ROOMY))
        .await;
    assert_eq!(single.bytes, b"one name");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn a_fifo_a_device_and_a_socket_cannot_be_read_and_do_not_stall_listing_or_search() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrspecial");
    let tree = PolygonHomeTree::new(&account);
    tree.fifo("pipe-entry");
    tree.device("device-entry");
    let (_, _listener) = tree.socket("socket-entry");
    tree.file("regular-entry", b"I am a file");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    // Listing and search only `lstat` an entry that is not a directory. A FIFO
    // with no writer would block an open for ever, a device open has side
    // effects; both calls returning inside the budget is the observation.
    let listed = session
        .list(BrowseSession::list_request(name, ""))
        .await
        .unwrap();
    for special in ["pipe-entry", "device-entry", "socket-entry"] {
        let entry = listed
            .entries
            .iter()
            .find(|entry| entry.name == special)
            .unwrap_or_else(|| panic!("{special} is listed"));
        assert!(
            !entry.is_directory && !entry.is_symlink,
            "{special} is neither a directory nor a link"
        );
    }
    let found = session
        .search(BrowseSession::search_request(name, "", "-entry"))
        .await
        .unwrap();
    assert_eq!(found.matches.len(), 4, "all four entries match the query");

    // A read opens with O_NONBLOCK, so the FIFO answers at once, refused.
    for special in ["pipe-entry", "device-entry", "socket-entry"] {
        let read = session
            .read(BrowseSession::read_request(name, special, ROOMY))
            .await;
        BrowseSession::assert_refused(
            &read
                .error
                .unwrap_or_else(|| panic!("{special} must be refused")),
        );
        assert!(read.bytes.is_empty());
    }

    // Control: the regular file beside them reads.
    let regular = session
        .read(BrowseSession::read_request(name, "regular-entry", ROOMY))
        .await;
    assert_eq!(regular.bytes, b"I am a file");
}

#[tokio::test(flavor = "multi_thread")]
#[ignore = "creates a real system account and drops privileges: polygon only"]
async fn search_never_descends_into_a_symlinked_directory() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("pbrsearchlink");
    let tree = PolygonHomeTree::new(&account);
    tree.symlink("tree/link-to-etc", "/etc");
    tree.file("tree/real/passwd-copy", b"mine");
    let session = BrowseSession::start().await;
    let name = account.name().as_str();

    let found = session
        .search(BrowseSession::search_request(name, "tree", "passwd"))
        .await
        .unwrap();
    let paths: Vec<_> = found.matches.iter().map(|m| m.path.clone()).collect();
    // Control: the real nested directory was searched.
    assert_eq!(paths, ["tree/real/passwd-copy"]);

    // The link itself is reported as a link when its NAME matches.
    let named = session
        .search(BrowseSession::search_request(name, "tree", "link-to"))
        .await
        .unwrap();
    assert_eq!(named.matches.len(), 1);
    let entry = named.matches[0].entry.as_ref().unwrap();
    assert!(entry.is_symlink && !entry.is_directory);
}
