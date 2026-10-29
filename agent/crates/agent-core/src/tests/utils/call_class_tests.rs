//! Tests for [`CallClass`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::utils::call_class::CallClass;

/// Every class maps to the deadline its table pins.
#[test]
fn every_call_class_deadline_is_the_pinned_value() {
    assert_eq!(CallClass::Query.deadline(), Duration::from_secs(60));
    assert_eq!(CallClass::Validate.deadline(), Duration::from_secs(60));
    assert_eq!(CallClass::Reload.deadline(), Duration::from_secs(240));
    assert_eq!(CallClass::Mutation.deadline(), Duration::from_secs(120));
    assert_eq!(CallClass::Database.deadline(), Duration::from_secs(300));
    assert_eq!(
        CallClass::PackageInstall.deadline(),
        Duration::from_secs(2_100)
    );
    assert_eq!(CallClass::Listing.deadline(), Duration::from_secs(3_600));
    assert_eq!(CallClass::Bulk.deadline(), Duration::from_secs(43_200));
}

/// A pre-scan listing is bounded well below a bulk job, so an archive that
/// expands enormously cannot hold the backup lock for twelve hours.
#[test]
fn a_listing_is_bounded_well_below_a_bulk_job() {
    assert!(CallClass::Listing.deadline() < CallClass::Bulk.deadline());
}

/// A reload outlasts the service manager's default stop plus start timeouts, so
/// the manager's own verdict arrives before the agent kills its client.
#[test]
fn a_reload_outlasts_systemds_default_stop_plus_start_timeouts() {
    assert!(CallClass::Reload.deadline() > Duration::from_secs(90 + 90));
}

/// The package install unit may run 1800 s and then stop for 90 s; the deadline
/// is above that sum and never equal to it.
#[test]
fn a_package_install_outlasts_its_units_start_timeout_plus_stop_timeout() {
    assert!(CallClass::PackageInstall.deadline() > Duration::from_secs(1_800 + 90));
}

/// A pure validator holds the host-wide configuration lock, so it is bounded
/// well below a reload.
#[test]
fn a_pure_validator_is_bounded_well_below_a_reload() {
    assert!(CallClass::Validate.deadline() < CallClass::Reload.deadline());
}

/// No class is shorter than a state query, and the data-scaled class is the
/// longest of all.
#[test]
fn no_class_is_shorter_than_query() {
    let all = [
        CallClass::Query,
        CallClass::Validate,
        CallClass::Reload,
        CallClass::Mutation,
        CallClass::Database,
        CallClass::PackageInstall,
        CallClass::Listing,
        CallClass::Bulk,
    ];

    for class in all {
        assert!(
            class.deadline() >= CallClass::Query.deadline(),
            "{class:?} is shorter than a query"
        );
        assert!(
            class.deadline() <= CallClass::Bulk.deadline(),
            "{class:?} is longer than a bulk run"
        );
    }
}
