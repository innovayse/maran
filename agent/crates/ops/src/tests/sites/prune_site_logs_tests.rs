//! Tests for what a prune removes and, above all, what it leaves.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::log_retention::LogRetention;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::prune_site_logs;
use crate::sites::SitesOpError;
use crate::sites::fake_site_log_host::FakeSiteLogHost;
use crate::sites::model::prune_logs_input::PruneLogsInput;

fn prune(host: &FakeSiteLogHost, domain: &str, retain: u32) -> Result<u32, SitesOpError> {
    prune_site_logs(
        host,
        &PruneLogsInput {
            account: AccountName::parse("acme").unwrap(),
            domain: Domain::parse(domain).unwrap(),
            retain: LogRetention::parse(retain).unwrap(),
        },
    )
    .map(|pruned| pruned.removed_files)
}

/// The live files and three generations of both logs for `site`.
fn site_files(host: &FakeSiteLogHost, site: &str) {
    host.put(&format!("{site}.access.log"), b"live");
    host.put(&format!("{site}.error.log"), b"live");
    host.put(&format!("{site}.access.log.1"), b"one");
    host.put(&format!("{site}.access.log.2.gz"), b"two");
    host.put(&format!("{site}.access.log.3.gz"), b"three");
    host.put(&format!("{site}.error.log.1"), b"one");
    host.put(&format!("{site}.error.log.2.gz"), b"two");
    host.put(&format!("{site}.error.log.3.gz"), b"three");
}

#[test]
fn archives_above_the_retention_are_removed_and_the_rest_stay() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");

    let removed = prune(&host, "a.com", 1).unwrap();

    assert_eq!(removed, 4);
    assert_eq!(
        host.names(),
        vec![
            "a.com.access.log",
            "a.com.access.log.1",
            "a.com.error.log",
            "a.com.error.log.1",
        ]
    );
}

#[test]
fn the_accept_side_a_compressed_third_generation_is_removed_at_a_retention_of_two() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");

    assert_eq!(prune(&host, "a.com", 2).unwrap(), 2);
    assert!(!host.names().contains(&"a.com.access.log.3.gz".to_owned()));
    assert!(host.names().contains(&"a.com.access.log.2.gz".to_owned()));
}

#[test]
fn the_live_file_is_never_removed_whatever_the_retention() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");

    prune(&host, "a.com", 1).unwrap();

    assert!(host.names().contains(&"a.com.access.log".to_owned()));
    assert!(host.names().contains(&"a.com.error.log".to_owned()));
}

#[test]
fn three_sites_whose_names_extend_one_another_each_lose_only_their_own_files() {
    for pruned_site in ["a.com", "a.com.access", "a.com.access.log"] {
        let host = FakeSiteLogHost::new();
        for site in ["a.com", "a.com.access", "a.com.access.log"] {
            site_files(&host, site);
        }
        let before = host.names();

        let removed = prune(&host, pruned_site, 1).unwrap();

        assert_eq!(removed, 4, "{pruned_site}");
        let mut gone: Vec<String> = before
            .into_iter()
            .filter(|name| !host.names().contains(name))
            .collect();
        gone.sort();
        let mut expected: Vec<String> = [
            "access.log.2.gz",
            "access.log.3.gz",
            "error.log.2.gz",
            "error.log.3.gz",
        ]
        .iter()
        .map(|suffix| format!("{pruned_site}.{suffix}"))
        .collect();
        expected.sort();
        assert_eq!(
            gone, expected,
            "{pruned_site} removed a file that is not its own"
        );
    }
}

#[test]
fn look_alikes_and_files_that_are_not_rotated_logs_are_left_alone() {
    let host = FakeSiteLogHost::new();
    for name in [
        "a.com.access.log.1.bak",
        "a.com.access.log.x",
        "a.com.access.log.02",
        "a.com.access.log.0",
        "a.com.access.log.5.gz.old",
        "notes.txt",
        "other.example.access.log.9",
    ] {
        host.put(name, b"keep");
    }
    host.put_directory("a.com.access.log.2");
    host.put_directory("a.com.error.log.7.gz");
    let before = host.names();

    let removed = prune(&host, "a.com", 1).unwrap();

    assert_eq!(removed, 0);
    assert_eq!(host.names(), before, "a directory is never removed");
}

#[test]
fn the_maximum_retention_still_removes_a_generation_above_it() {
    let host = FakeSiteLogHost::new();
    host.put("a.com.access.log.14.gz", b"kept");
    host.put("a.com.access.log.15.gz", b"removed");

    assert_eq!(prune(&host, "a.com", 14).unwrap(), 1);
    assert_eq!(host.names(), vec!["a.com.access.log.14.gz"]);
}

#[test]
fn pruning_twice_removes_nothing_the_second_time() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");

    assert_eq!(prune(&host, "a.com", 1).unwrap(), 4);
    assert_eq!(prune(&host, "a.com", 1).unwrap(), 0);
}

#[test]
fn an_empty_directory_is_a_success_that_removed_nothing() {
    assert_eq!(prune(&FakeSiteLogHost::new(), "a.com", 3).unwrap(), 0);
}

#[test]
fn a_directory_root_does_not_own_is_an_error_and_removes_nothing() {
    let host = FakeSiteLogHost::new();
    site_files(&host, "a.com");
    let before = host.names();
    host.refuse_the_directory();

    assert!(matches!(
        prune(&host, "a.com", 1),
        Err(SitesOpError::LogUnreadable { .. })
    ));
    assert_eq!(host.names(), before);
}
