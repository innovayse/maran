//! Tests for [`parse_passwd`]: reading the account database of a source.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::{count_of, kinds_of};
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::parse_passwd::parse_passwd;

/// Hand-written account database (synthetic).
const HOSTING: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/passwd_hosting.txt");

/// Hand-written account database full of refusable entries (synthetic).
const HOSTILE: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/passwd_hostile.txt");

/// The bytes of a database whose first name is not UTF-8 (synthetic).
const NON_UTF8: &[u8] =
    include_bytes!("../../../../tests/fixtures/transfer/synthetic_inventory/passwd_non_utf8.bin");

/// Names of the entries kept, in order.
fn names(listing: &str) -> Vec<String> {
    parse_passwd(listing)
        .items
        .iter()
        .map(|entry| entry.name.as_str().to_owned())
        .collect()
}

/// Regular accounts with a real shell, or a home under /home, are kept.
#[test]
fn hosting_accounts_are_kept_and_system_accounts_are_not() {
    let parsed = parse_passwd(HOSTING);

    assert_eq!(names(HOSTING), ["acme", "shopsite", "deploy"]);
    assert!(parsed.warnings.is_empty());
    assert_eq!(parsed.items[0].home.as_str(), "/home/acme");
}

/// A service account with a nologin shell and a home elsewhere is dropped, and
/// the same entry with a /home home is the inverse control: kept.
#[test]
fn a_nologin_account_is_kept_only_when_its_home_is_under_home() {
    assert!(names("svcaccount:x:1500:1500::/srv/svc:/usr/sbin/nologin\n").is_empty());
    assert_eq!(
        names("svcaccount:x:1500:1500::/home/svcaccount:/usr/sbin/nologin\n"),
        ["svcaccount"]
    );
}

/// Ids below 1000 and nobody are never hosting accounts; 1000 is the inverse
/// control.
#[test]
fn only_regular_user_ids_are_accounts() {
    assert!(names("lowuid:x:999:999::/home/lowuid:/bin/bash\n").is_empty());
    assert!(names("nobodyx:x:65534:65534::/home/nobodyx:/bin/bash\n").is_empty());
    assert_eq!(
        names("firstuid:x:1000:1000::/home/firstuid:/bin/bash\n"),
        ["firstuid"]
    );
}

/// Names that fail the account-name validator are refused with a warning that
/// carries no text, and the valid account is still read.
#[test]
fn names_the_validator_refuses_are_warnings_never_repaired() {
    let parsed = parse_passwd(HOSTILE);

    assert_eq!(names(HOSTILE), ["goodacct"]);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::NameRefused),
        parsed.warnings.len()
    );
    // Alice, john-doe, ab (no subject), spacey (home refused, subject is the
    // validated name) and one aggregate for the two malformed lines.
    assert_eq!(parsed.warnings.len(), 5);
    assert!(parsed.warnings.iter().any(|w| w.subject() == "spacey"));
    assert!(
        parsed
            .warnings
            .iter()
            .filter(|w| w.subject().is_empty())
            .count()
            == 4
    );
}

/// A repeated name keeps its first entry.
#[test]
fn a_repeated_name_keeps_its_first_entry() {
    let parsed = parse_passwd(HOSTILE);

    assert_eq!(parsed.items[0].home.as_str(), "/home/goodacct");
}

/// A line with a NUL byte is refused, never read, and costs one warning.
#[test]
fn a_nul_byte_line_is_refused() {
    let listing =
        "acme:x:1001:1001::/home/acme:/bin/bash\nev\0il:x:1002:1002::/home/evil:/bin/bash\n";

    let parsed = parse_passwd(listing);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(kinds_of(&parsed.warnings), [SourceWarningKind::NameRefused]);
}

/// A huge line is refused and does not stop the lines after it.
#[test]
fn a_huge_line_is_refused_and_the_rest_is_read() {
    let listing = format!(
        "{}\nacme:x:1001:1001::/home/acme:/bin/bash\n",
        "x".repeat(InventoryLimits::MAX_LINE_BYTES * 4)
    );

    let parsed = parse_passwd(&listing);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::NameRefused),
        1
    );
}

/// Text that was not UTF-8 reaches the parser as replacement characters, which
/// no account name may hold: the entry is refused, the next one is read.
#[test]
fn a_non_utf8_name_is_refused_and_the_next_account_is_read() {
    let text = String::from_utf8_lossy(NON_UTF8);

    let parsed = parse_passwd(&text);

    assert_eq!(parsed.items.len(), 1);
    assert_eq!(parsed.items[0].name.as_str(), "fine");
    assert_eq!(
        count_of(&parsed.warnings, SourceWarningKind::NameRefused),
        1
    );
}

/// More accounts than the bound stops the read cleanly with Truncated.
#[test]
fn too_many_accounts_stop_the_read_with_truncated() {
    let mut listing = String::new();
    for index in 0..(InventoryLimits::MAX_ACCOUNTS + 3) {
        listing.push_str(&format!(
            "u{index:05}:x:{}:1000::/home/u{index:05}:/bin/bash\n",
            2000 + index
        ));
    }

    let parsed = parse_passwd(&listing);

    assert_eq!(parsed.items.len(), InventoryLimits::MAX_ACCOUNTS);
    assert_eq!(count_of(&parsed.warnings, SourceWarningKind::Truncated), 1);
}

/// Exactly the bound is the inverse control: every account kept, no warning.
#[test]
fn exactly_the_account_bound_is_not_truncated() {
    let mut listing = String::new();
    for index in 0..InventoryLimits::MAX_ACCOUNTS {
        listing.push_str(&format!(
            "u{index:05}:x:{}:1000::/home/u{index:05}:/bin/bash\n",
            2000 + index
        ));
    }

    let parsed = parse_passwd(&listing);

    assert_eq!(parsed.items.len(), InventoryLimits::MAX_ACCOUNTS);
    assert!(parsed.warnings.is_empty());
}

/// An empty database yields nothing and no warning.
#[test]
fn an_empty_listing_yields_nothing() {
    let parsed = parse_passwd("");

    assert!(parsed.items.is_empty());
    assert!(parsed.warnings.is_empty());
}
