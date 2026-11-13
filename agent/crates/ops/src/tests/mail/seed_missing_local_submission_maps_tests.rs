//! Seeding the local-submission maps an agent upgrade added.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::seed_missing_local_submission_maps;
use crate::mail::fake_mail_host::{FakeMailHost, distro};

const ACCOUNTS: &str = "/etc/maran/mail/local_submit_accounts.map";
const FANOUT: &str = "/etc/maran/mail/alias_fanout.map";

/// An upgraded host has the four earlier maps and not the fan-out map: it is seeded
/// empty at the agent's start, and the populated maps are not touched.
#[test]
fn an_enabled_host_missing_only_the_fan_out_map_gets_it_empty() {
    let host = FakeMailHost::new().with_file(ACCOUNTS, "1001 acme\n");

    let enabled = seed_missing_local_submission_maps(&host, distro()).unwrap();

    assert!(enabled);
    assert_eq!(host.file(FANOUT).as_deref(), Some("# no entries\n"));
    assert_eq!(host.file(ACCOUNTS).as_deref(), Some("1001 acme\n"));
}

/// Inverse controls: a host that never enabled mail gets nothing, and a fan-out map that
/// already holds an alias is not emptied.
#[test]
fn a_host_without_mail_gets_nothing_and_an_existing_fan_out_map_is_kept() {
    let bare = FakeMailHost::new();
    assert!(!seed_missing_local_submission_maps(&bare, distro()).unwrap());
    assert_eq!(bare.file(FANOUT), None);

    let populated = FakeMailHost::new()
        .with_file(ACCOUNTS, "1001 acme\n")
        .with_file(FANOUT, "team@a.test 5 acme\n");
    seed_missing_local_submission_maps(&populated, distro()).unwrap();
    assert_eq!(
        populated.file(FANOUT).as_deref(),
        Some("team@a.test 5 acme\n")
    );
}
