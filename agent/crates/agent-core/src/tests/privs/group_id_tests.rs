//! Tests for resolving a system group's numeric id.
//!
//! Three propositions, and the third is the one worth the file: a group that
//! resolves to gid 0 is REFUSED. A restore re-applies this gid to an account's
//! home root, so a root-grouped answer would hand a customer's home to a group
//! nothing about it should belong to — and it would do so on the success path.
//!
//! The refusals have an inverse control beside them: a real group from this
//! host's own database must be ACCEPTED, or a lookup mutated to refuse
//! everything would pass every test here (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::GroupId;
use crate::privs::priv_error::PrivError;

/// A group this host really has, that is not root's: name and gid, read from
/// the local database.
///
/// Read rather than hardcoded because the group names differ between families
/// and this test runs on both. Returns `None` on a host whose database is
/// unreadable or holds nothing but root, in which case the acceptance test
/// says so rather than passing silently.
fn a_real_non_root_group() -> Option<(String, u32)> {
    let contents = std::fs::read_to_string("/etc/group").ok()?;

    contents.lines().find_map(|line| {
        let mut fields = line.split(':');
        let name = fields.next()?.to_owned();
        let _password = fields.next()?;
        let gid: u32 = fields.next()?.parse().ok()?;

        (gid != 0 && !name.is_empty()).then_some((name, gid))
    })
}

#[test]
fn a_group_this_host_has_resolves_to_the_gid_its_database_records() {
    let Some((name, gid)) = a_real_non_root_group() else {
        panic!("this host's group database holds no non-root group to check against");
    };

    let resolved = GroupId::resolve(&name).expect("a group in the database resolves");

    assert_eq!(resolved.gid(), gid);
}

#[test]
fn the_root_group_is_refused_rather_than_returned() {
    let error = GroupId::resolve("root").expect_err("gid 0 is refused");

    assert_eq!(error, PrivError::RootGroup);
}

#[test]
fn a_group_this_host_does_not_have_is_reported_as_absent() {
    let error = GroupId::resolve("maran_no_such_group_exists_here")
        .expect_err("an absent group is an error");

    assert_eq!(error, PrivError::NoSuchGroup);
}

#[test]
fn a_name_that_cannot_be_a_c_string_is_reported_as_absent_and_never_truncated() {
    let error = GroupId::resolve("www\0data").expect_err("an embedded NUL is refused");

    // Refused, not passed on as "www": a name truncated at the NUL would
    // resolve a DIFFERENT group from the one asked about.
    assert_eq!(error, PrivError::NoSuchGroup);
}
