//! The jail's mount unit: written once, started every time, and every service
//! manager call bounded and guarded.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;

use crate::ftps::ensure_account_jail::ensure_account_jail;
use crate::ftps::fake_ftps_host::{FakeFtpsHost, Spawn, distro};
use crate::ftps::ftps_error::FtpsError;

/// The account every test here builds a jail for.
const ACCOUNT: &str = "alice";

/// The calls made to the service manager, in order.
fn service_manager_calls(host: &FakeFtpsHost) -> Vec<Spawn> {
    host.spawns()
        .into_iter()
        .filter(|spawn| spawn.argv[0] == distro().service_manager().as_str())
        .collect()
}

#[test]
fn a_new_jail_reloads_the_service_manager_and_then_starts_its_unit_behind_the_marker() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);
    let jail = host.jail();

    ensure_account_jail(&host, distro(), &jail).expect("the jail is built");

    let calls = service_manager_calls(&host);
    assert_eq!(calls.len(), 2, "{calls:?}");
    assert_eq!(calls[0].argv[1..], ["daemon-reload"]);
    let expected_start = ["enable", "--now", "--", jail.unit_name()];
    assert_eq!(calls[1].argv[1..], expected_start);
}

#[test]
fn every_service_manager_call_of_a_new_jail_is_a_reload_and_none_is_unbounded() {
    // `daemon-reload` and `enable --now` change the service manager's state, so
    // they run under the reload deadline; an absent class would be the one call
    // here with no deadline at all.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    ensure_account_jail(&host, distro(), &host.jail()).expect("the jail is built");

    let calls = service_manager_calls(&host);
    assert!(!calls.is_empty());
    for call in calls {
        assert_eq!(call.class, Some(CallClass::Reload), "{:?}", call.argv);
    }
}

#[test]
fn an_unchanged_jail_is_started_again_behind_the_marker_but_its_unit_is_not_rewritten() {
    // The write path's reload step is `enable --now`; re-running it over an
    // identical unit would restart a bind mount under live logins. The start is
    // still issued, because it is the only thing that brings up a mount that
    // somebody stopped by hand.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);
    let jail = host.jail();
    ensure_account_jail(&host, distro(), &jail).expect("the jail is built");
    let calls_before = service_manager_calls(&host).len();

    ensure_account_jail(&host, distro(), &jail).expect("the jail is unchanged");

    assert_eq!(host.written_unit_paths().len(), 1, "written exactly once");
    let calls = service_manager_calls(&host);
    assert_eq!(calls.len(), calls_before + 1, "{calls:?}");
    let start = calls.last().unwrap();
    let expected_start = ["enable", "--now", "--", jail.unit_name()];
    assert_eq!(start.argv[1..], expected_start);
    assert_eq!(start.class, Some(CallClass::Reload));
}

#[test]
fn a_unit_write_that_times_out_is_a_bound_and_not_a_jail_that_could_not_be_built() {
    // A killed `daemon-reload` or `enable` has not said the mount cannot come
    // up. Folding it into `JailFailed` would send an operator looking for a
    // configuration fault on a host that was only slow.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).with_config_writes_timing_out();

    let failed = ensure_account_jail(&host, distro(), &host.jail()).expect_err("timed out");

    assert!(matches!(failed, FtpsError::Bound(_)), "got {failed:?}");
}

#[test]
fn a_refused_unit_write_stays_a_jail_failure_and_is_not_mistaken_for_a_bound() {
    // The inverse control of the test above: a mechanical failure of the write
    // protocol is still `JailFailed`, so a mapping that sent everything to
    // `Bound` is caught.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).unwritable_config();

    let failed = ensure_account_jail(&host, distro(), &host.jail()).expect_err("refused");

    assert_eq!(failed, FtpsError::JailFailed);
}
