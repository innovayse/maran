//! Tests for `list_pending_updates`. Mirrors the source tree (rules/testing.md).
//!
//! The outputs the fake package manager prints are the recordings under
//! `crates/distro/tests/fixtures/package_manager/`, taken from real
//! `ubuntu:24.04` and `almalinux:9` containers; this file decides what the
//! operation does with them. The three ways the read can fail are tested
//! separately on purpose (a non-zero status, a line that does not parse, a run
//! that was stopped), because each must be an error and none may become an empty
//! list.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{PendingUpdatesFailure, UpdateClassification};

use super::list_pending_updates;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro, rhel_distro, suse_distro};
use crate::monitor::monitor_error::MonitorError;

/// The simulated upgrade after `apt-get update` on an old image: 67 updates.
const APT_WITH_UPDATES: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/apt_dist_upgrade_with_updates.txt"
);

/// The simulated upgrade of a host with nothing to upgrade.
const APT_UP_TO_DATE: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/apt_dist_upgrade_up_to_date.txt"
);

/// What `apt-get indextargets` prints when the lists were built.
const APT_INDEXES: &str =
    include_str!("../../../../distro/tests/fixtures/package_manager/apt_indextargets_built.txt");

/// `dnf check-update -C -q` after the cache was built: 115 updates, exit 100.
const DNF_WITH_UPDATES: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_check_update_with_updates.txt"
);

/// The security-only reading of the same cache: names two of the 115.
const DNF_SECURITY_ONLY: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_check_update_security_only.txt"
);

/// The security-only reading of a cache holding no security update: empty.
const DNF_SECURITY_NONE: &str = include_str!(
    "../../../../distro/tests/fixtures/package_manager/dnf_check_update_security_none.txt"
);

/// The arguments of the RHEL family's first reading.
const DNF_FIRST_READING: [&str; 3] = ["check-update", "-C", "-q"];

/// The arguments of the RHEL family's second, security-only reading.
const DNF_SECURITY_READING: [&str; 4] = ["check-update", "--security", "-C", "-q"];

/// The fake answers a run by its LAST argument: this one ends the Debian
/// family's simulation.
const APT_SIMULATION: &str = "dist-upgrade";

/// The last argument of the Debian family's catalogue probe.
const APT_PROBE: &str = "$(FILENAME)";

/// A Debian-family host whose catalogue exists and whose simulation printed
/// `simulation` and exited 0.
fn apt_host_printing(simulation: &str) -> FakeMonitorHost {
    FakeMonitorHost::from_ubuntu_captures()
        .with_command_answer(APT_PROBE, 0, APT_INDEXES)
        .with_command_answer(APT_SIMULATION, 0, simulation)
}

/// A RHEL-family host whose cache-only check exited `status` and printed
/// `stdout`, and whose security-only reading found nothing. The two readings
/// both end in `-q`, so each is answered by its whole argument list.
fn dnf_host_answering(status: i32, stdout: &str) -> FakeMonitorHost {
    dnf_host_with_security_reading(status, stdout, 0, DNF_SECURITY_NONE)
}

/// A RHEL-family host whose first reading exited `status` printing `stdout`
/// and whose security-only reading exited `security_status` printing
/// `security_stdout`.
fn dnf_host_with_security_reading(
    status: i32,
    stdout: &str,
    security_status: i32,
    security_stdout: &str,
) -> FakeMonitorHost {
    FakeMonitorHost::from_alma_captures()
        .with_exact_command_answer(&DNF_FIRST_READING, status, stdout)
        .with_exact_command_answer(&DNF_SECURITY_READING, security_status, security_stdout)
}

/// Whether every entry of `updates` carries `classification`.
fn all_are(updates: &[maran_distro::PendingUpdate], classification: UpdateClassification) -> bool {
    updates
        .iter()
        .all(|update| update.classification == classification)
}

#[test]
fn a_debian_host_with_pending_updates_lists_them() {
    let host = apt_host_printing(APT_WITH_UPDATES);

    let updates = list_pending_updates(&host, distro()).unwrap();

    assert_eq!(updates.len(), 67);
    assert_eq!(updates[0].package_name, "gcc-14-base");
}

#[test]
fn a_debian_host_with_nothing_to_upgrade_is_an_empty_list() {
    let host = apt_host_printing(APT_UP_TO_DATE);

    let updates = list_pending_updates(&host, distro()).unwrap();

    assert!(updates.is_empty());
}

#[test]
fn a_rhel_host_with_pending_updates_lists_them() {
    let host = dnf_host_answering(100, DNF_WITH_UPDATES);

    let updates = list_pending_updates(&host, rhel_distro()).unwrap();

    assert_eq!(updates.len(), 115);
    assert_eq!(updates[0].package_name, "acl");
}

#[test]
fn a_rhel_host_with_nothing_to_upgrade_is_an_empty_list() {
    let host = dnf_host_answering(0, "");

    let updates = list_pending_updates(&host, rhel_distro()).unwrap();

    assert!(updates.is_empty());
}

/// The recorded fact: against an empty lists directory the simulation exits 0 and
/// says "0 upgraded", so without the probe this host would read as up to date.
#[test]
fn a_debian_host_whose_catalogue_was_never_built_is_an_error_and_not_an_empty_list() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_command_answer(APT_PROBE, 0, "")
        .with_command_answer(APT_SIMULATION, 0, APT_UP_TO_DATE);

    let result = list_pending_updates(&host, distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::CatalogueNotBuilt
        })
    );
}

#[test]
fn the_simulation_is_not_run_when_the_catalogue_is_missing() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_command_answer(APT_PROBE, 0, "")
        .with_command_answer(APT_SIMULATION, 0, APT_WITH_UPDATES);

    let _ = list_pending_updates(&host, distro());

    assert_eq!(host.commands().len(), 1, "{:?}", host.commands());
}

/// A cache that was never built makes `dnf check-update -C` exit 1.
#[test]
fn a_rhel_host_without_a_cache_is_an_error_and_not_an_empty_list() {
    let host = dnf_host_answering(1, "");

    let result = list_pending_updates(&host, rhel_distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::Unsuccessful { status: 1 }
        })
    );
}

#[test]
fn a_non_zero_status_from_the_simulation_is_an_error_and_not_an_empty_list() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_command_answer(APT_PROBE, 0, APT_INDEXES)
        .with_command_answer(APT_SIMULATION, 100, "");

    let result = list_pending_updates(&host, distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::Unsuccessful { status: 100 }
        })
    );
}

#[test]
fn a_non_zero_status_from_the_catalogue_probe_is_an_error() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_command_answer(APT_PROBE, 2, "")
        .with_command_answer(APT_SIMULATION, 0, APT_UP_TO_DATE);

    let result = list_pending_updates(&host, distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::Unsuccessful { status: 2 }
        })
    );
}

#[test]
fn an_unparsable_line_from_apt_is_an_error_and_not_a_shorter_list() {
    let printed = format!("{APT_WITH_UPDATES}an unknown line\n");
    let host = apt_host_printing(&printed);

    let result = list_pending_updates(&host, distro());

    assert!(
        matches!(
            result,
            Err(MonitorError::UpdateCatalogueUnavailable {
                reason: PendingUpdatesFailure::UnrecognisedLine { .. }
            })
        ),
        "{result:?}"
    );
}

#[test]
fn an_unparsable_line_from_dnf_is_an_error_and_not_a_shorter_list() {
    let printed = format!("{DNF_WITH_UPDATES}a-row-with-one-field\n");
    let host = dnf_host_answering(100, &printed);

    let result = list_pending_updates(&host, rhel_distro());

    assert!(
        matches!(
            result,
            Err(MonitorError::UpdateCatalogueUnavailable {
                reason: PendingUpdatesFailure::UnrecognisedLine { .. }
            })
        ),
        "{result:?}"
    );
}

#[test]
fn a_package_manager_that_cannot_start_is_an_error_and_not_an_empty_list() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_absent_service_manager();

    let result = list_pending_updates(&host, distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::NotStarted
        })
    );
}

/// A run killed at its deadline has said nothing about the host: it is the bound,
/// carried through by name, and neither "not started" nor "no updates".
#[test]
fn a_run_killed_at_its_deadline_is_a_bound_and_not_an_empty_list() {
    for adapter in [distro(), rhel_distro(), suse_distro()] {
        let host = FakeMonitorHost::from_ubuntu_captures().with_timed_out_service_manager();

        let result = list_pending_updates(&host, adapter);

        assert!(matches!(result, Err(MonitorError::Bound(_))), "{result:?}");
    }
}

/// The exact argv of the Debian and RHEL families, and the one class they run in. These are
/// the constants that keep the run a simulation (`-s`) and cache-only (`-C`),
/// so an edit that drops either turns this red.
#[test]
fn the_debian_run_is_a_simulation_and_the_rhel_run_is_cache_only() {
    let apt = apt_host_printing(APT_UP_TO_DATE);
    list_pending_updates(&apt, distro()).unwrap();
    let dnf = dnf_host_answering(0, "");
    list_pending_updates(&dnf, rhel_distro()).unwrap();

    assert_eq!(
        apt.commands(),
        vec![
            vec![
                String::from("/usr/bin/apt-get"),
                String::from("indextargets"),
                String::from("--format"),
                String::from("$(FILENAME)"),
            ],
            vec![
                String::from("/usr/bin/apt-get"),
                String::from("-s"),
                String::from("-q"),
                String::from("-o"),
                String::from("Debug::NoLocking=true"),
                String::from("dist-upgrade"),
            ],
        ]
    );
    assert_eq!(
        dnf.commands(),
        vec![
            vec![
                String::from("/usr/bin/dnf"),
                String::from("check-update"),
                String::from("-C"),
                String::from("-q"),
            ],
            vec![
                String::from("/usr/bin/dnf"),
                String::from("check-update"),
                String::from("--security"),
                String::from("-C"),
                String::from("-q"),
            ],
        ]
    );
}

/// The class is the query class on every run of both families: the deadline is
/// the table's, not this operation's.
#[test]
fn every_run_is_in_the_query_class() {
    let apt = apt_host_printing(APT_UP_TO_DATE);
    list_pending_updates(&apt, distro()).unwrap();
    let dnf = dnf_host_answering(0, "");
    list_pending_updates(&dnf, rhel_distro()).unwrap();

    assert_eq!(apt.classes(), vec![CallClass::Query, CallClass::Query]);
    assert_eq!(dnf.classes(), vec![CallClass::Query, CallClass::Query]);
}

/// A Debian entry is classified from the origins printed in the same output: a
/// suite ending `-security` among them is security, any other list ordinary.
#[test]
fn a_debian_update_published_by_the_security_suite_is_security_and_the_rest_ordinary() {
    let host = apt_host_printing(APT_WITH_UPDATES);

    let updates = list_pending_updates(&host, distro()).unwrap();

    let named = |name: &str| {
        updates
            .iter()
            .find(|update| update.package_name == name)
            .unwrap()
            .classification
    };
    assert_eq!(named("gcc-14-base"), UpdateClassification::Security);
    assert_eq!(named("base-files"), UpdateClassification::Ordinary);
}

/// The first reading already classifies, so the Debian family asks no second
/// question: the probe and the simulation are the only two runs.
#[test]
fn a_debian_host_is_not_asked_a_second_reading() {
    let host = apt_host_printing(APT_WITH_UPDATES);

    list_pending_updates(&host, distro()).unwrap();

    assert_eq!(host.commands().len(), 2, "{:?}", host.commands());
}

/// The second reading names two of the 115 packages: those are security and
/// every other row is a measured ordinary one.
#[test]
fn a_rhel_update_the_security_reading_names_is_security_and_the_rest_ordinary() {
    let host = dnf_host_with_security_reading(100, DNF_WITH_UPDATES, 100, DNF_SECURITY_ONLY);

    let updates = list_pending_updates(&host, rhel_distro()).unwrap();

    let security: Vec<&str> = updates
        .iter()
        .filter(|update| update.classification == UpdateClassification::Security)
        .map(|update| update.package_name.as_str())
        .collect();
    assert_eq!(updates.len(), 115);
    assert_eq!(security, vec!["curl-minimal", "libcurl-minimal"]);
    assert!(
        updates
            .iter()
            .filter(|update| update.package_name != "curl-minimal")
            .filter(|update| update.package_name != "libcurl-minimal")
            .all(|update| update.classification == UpdateClassification::Ordinary)
    );
}

/// A security reading that succeeded and named nothing is a measurement:
/// every row is ordinary, and none is left unmeasured.
#[test]
fn a_rhel_host_whose_security_reading_names_nothing_has_every_update_ordinary() {
    let host = dnf_host_with_security_reading(100, DNF_WITH_UPDATES, 0, DNF_SECURITY_NONE);

    let updates = list_pending_updates(&host, rhel_distro()).unwrap();

    assert_eq!(updates.len(), 115);
    assert!(all_are(&updates, UpdateClassification::Ordinary));
}

/// The list is still returned when the second reading fails, and no row is
/// guessed ordinary: each way the second reading can fail leaves all of them
/// unmeasured.
#[test]
fn a_failed_security_reading_leaves_the_list_and_every_entry_unmeasured() {
    let hosts = [
        // A status the reader does not know.
        dnf_host_with_security_reading(100, DNF_WITH_UPDATES, 1, ""),
        // A line the reader does not know.
        dnf_host_with_security_reading(100, DNF_WITH_UPDATES, 100, "a-row-with-one-field\n"),
        // A run stopped at its deadline.
        FakeMonitorHost::from_alma_captures()
            .with_exact_command_answer(&DNF_FIRST_READING, 100, DNF_WITH_UPDATES)
            .with_timed_out_service_manager(),
        // A program that could not be started the second time.
        FakeMonitorHost::from_alma_captures()
            .with_exact_command_answer(&DNF_FIRST_READING, 100, DNF_WITH_UPDATES)
            .with_absent_service_manager(),
    ];

    for host in hosts {
        let updates = list_pending_updates(&host, rhel_distro()).unwrap();

        assert_eq!(updates.len(), 115);
        assert!(all_are(&updates, UpdateClassification::Unmeasured));
    }
}

/// A failed FIRST reading is still an error when the second would have
/// answered: the second reading cannot rescue what the first could not read.
#[test]
fn a_failed_first_rhel_reading_is_an_error_even_when_the_security_reading_answers() {
    let host = dnf_host_with_security_reading(1, "", 100, DNF_SECURITY_ONLY);

    let result = list_pending_updates(&host, rhel_distro());

    assert_eq!(
        result,
        Err(MonitorError::UpdateCatalogueUnavailable {
            reason: PendingUpdatesFailure::Unsuccessful { status: 1 }
        })
    );
}

/// The arguments of the SUSE family's first reading.
const ZYPPER_FIRST_READING: [&str; 3] = ["--non-interactive", "--no-refresh", "list-updates"];

/// The arguments of the SUSE family's second reading, which only checks that the patch metadata can
/// be read.
const ZYPPER_SECOND_READING: [&str; 5] = [
    "--non-interactive",
    "--no-refresh",
    "list-patches",
    "--category",
    "security",
];

/// What `zypper list-updates` prints with two updates pending, CONSTRUCTED from `man zypper`.
///
/// Not a recording: no SUSE container exists in this phase, so the recording on a real Leap image is
/// owed and the layout is UNVERIFIED. Confirm with
/// `zypper --non-interactive --no-refresh list-updates`.
const ZYPPER_WITH_UPDATES: &str = "\
Loading repository data...
Reading installed packages...
S | Repository             | Name     | Current Version | Available Version | Arch
--+------------------------+----------+-----------------+-------------------+-------
v | Main Update Repository | curl     | 8.6.0-1.1       | 8.6.0-2.1         | x86_64
v | Main Repository        | php8-fpm | 8.4.10-1.1      | 8.4.16-1.1        | x86_64
";

/// What `zypper list-updates` prints, with status 0, when nothing is pending. Constructed likewise.
const ZYPPER_UP_TO_DATE: &str = "\
Loading repository data...
Reading installed packages...
No updates found.
";

/// A SUSE-family host whose cache-only listing exited `status` printing `stdout`, and whose second
/// reading exited `second_status`.
///
/// The host's other captures are the AlmaLinux ones the fake already holds; the family has no
/// capture of its own, and these readings answer by their whole argument list.
fn zypper_host_answering(status: i32, stdout: &str, second_status: i32) -> FakeMonitorHost {
    FakeMonitorHost::from_alma_captures()
        .with_exact_command_answer(&ZYPPER_FIRST_READING, status, stdout)
        .with_exact_command_answer(&ZYPPER_SECOND_READING, second_status, "")
}

#[test]
fn a_suse_host_with_pending_updates_lists_them_and_leaves_every_one_unmeasured() {
    let host = zypper_host_answering(100, ZYPPER_WITH_UPDATES, 0);

    let updates = list_pending_updates(&host, suse_distro()).unwrap();

    assert_eq!(updates.len(), 2);
    assert_eq!(updates[0].package_name, "curl");
    assert_eq!(updates[1].package_name, "php8-fpm");
    // The family cannot say which package a security patch concerns, so the verdict is never
    // `Ordinary` for a row whose metadata was not read.
    assert!(all_are(&updates, UpdateClassification::Unmeasured));
}

#[test]
fn a_suse_host_with_nothing_to_upgrade_is_an_empty_list() {
    let host = zypper_host_answering(0, ZYPPER_UP_TO_DATE, 0);

    let updates = list_pending_updates(&host, suse_distro()).unwrap();

    assert!(updates.is_empty());
}

/// A run that printed no table and no sentence has not said the host is up to date: it is an error
/// and never an empty list, which is the false "up to date" the operation exists to prevent.
#[test]
fn a_suse_host_whose_listing_printed_nothing_is_an_error_and_not_an_empty_list() {
    let host = zypper_host_answering(0, "", 0);

    let result = list_pending_updates(&host, suse_distro());

    assert!(result.is_err(), "{result:?}");
}

/// A repository zypper skipped (status 106) is a reading that failed, not a list.
#[test]
fn a_suse_host_whose_listing_skipped_a_repository_is_an_error_and_not_an_empty_list() {
    let host = zypper_host_answering(106, ZYPPER_UP_TO_DATE, 0);

    let result = list_pending_updates(&host, suse_distro());

    assert!(result.is_err(), "{result:?}");
}

/// A second reading that failed leaves the first reading's rows as they were, unmeasured: the
/// entries are still listed and none of them is turned into a verdict.
#[test]
fn a_failed_second_suse_reading_keeps_every_update_unmeasured() {
    let host = zypper_host_answering(100, ZYPPER_WITH_UPDATES, 106);

    let updates = list_pending_updates(&host, suse_distro()).unwrap();

    assert_eq!(updates.len(), 2);
    assert!(all_are(&updates, UpdateClassification::Unmeasured));
}

/// The exact argv of both SUSE readings and the one class they run in: cache-only, no operand.
#[test]
fn the_suse_runs_are_cache_only_and_in_the_query_class() {
    let host = zypper_host_answering(0, ZYPPER_UP_TO_DATE, 0);

    list_pending_updates(&host, suse_distro()).unwrap();

    assert_eq!(
        host.commands(),
        vec![
            vec![
                String::from("/usr/bin/zypper"),
                String::from("--non-interactive"),
                String::from("--no-refresh"),
                String::from("list-updates"),
            ],
            vec![
                String::from("/usr/bin/zypper"),
                String::from("--non-interactive"),
                String::from("--no-refresh"),
                String::from("list-patches"),
                String::from("--category"),
                String::from("security"),
            ],
        ]
    );
    assert_eq!(host.classes(), vec![CallClass::Query, CallClass::Query]);
}
