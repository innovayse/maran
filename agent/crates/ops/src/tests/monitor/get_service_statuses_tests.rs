//! Tests for `get_service_statuses`: what the panel is told about the units it
//! watches.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_service_statuses;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro, rhel_distro};
use crate::monitor::{MonitorError, ServiceState, ServiceStatus};

/// A unit that is up.
const RUNNING: &str = "LoadState=loaded\nActiveState=active\nSubState=running\nTriggeredBy=";

/// A unit that is down and that nothing stands in for.
const DEAD: &str = "LoadState=loaded\nActiveState=inactive\nSubState=dead\nTriggeredBy=";

/// A socket that is holding a listening descriptor.
const LISTENING: &str = "LoadState=loaded\nActiveState=active\nSubState=listening\nTriggeredBy=";

/// The Debian family's SSH service before anybody has connected: inactive, with
/// its socket named as what starts it.
const AWAITING_SOCKET: &str =
    "LoadState=loaded\nActiveState=inactive\nSubState=dead\nTriggeredBy=ssh.socket";

/// The state reported for `unit`.
fn state_of(statuses: &[ServiceStatus], unit: &str) -> ServiceState {
    statuses
        .iter()
        .find(|status| status.unit == unit)
        .expect("the unit is in the managed set")
        .state
}

#[test]
fn every_managed_unit_is_reported_in_the_adapters_order() {
    let host = FakeMonitorHost::from_ubuntu_captures();

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");
    let units: Vec<&str> = statuses.iter().map(|status| status.unit.as_str()).collect();

    assert_eq!(units, distro().managed_units());
}

#[test]
fn an_active_unit_is_running() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit("nginx", RUNNING);

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "nginx"), ServiceState::Running);
}

#[test]
fn a_reloading_unit_is_running() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit(
        "nginx",
        "LoadState=loaded\nActiveState=reloading\nSubState=reload\nTriggeredBy=",
    );

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "nginx"), ServiceState::Running);
}

#[test]
fn a_stopped_service_is_an_answer_not_an_error() {
    // A monitor that returns an error when a service is down has inverted its
    // own purpose: the caller asked for exactly that fact and would be shown a
    // broken monitor instead of a broken service.
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit("nginx", DEAD);

    let statuses =
        get_service_statuses(&host, distro()).expect("a stopped service is not a failed call");

    assert_eq!(state_of(&statuses, "nginx"), ServiceState::Stopped);
}

#[test]
fn a_failed_unit_is_stopped() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit(
        "mariadb",
        "LoadState=loaded\nActiveState=failed\nSubState=failed\nTriggeredBy=",
    );

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "mariadb"), ServiceState::Stopped);
}

#[test]
fn a_socket_activated_unit_that_has_never_been_triggered_is_not_an_outage() {
    // On the Debian family `ssh.socket` is the enabled unit and it holds the
    // listening descriptor. `ssh.service` is inactive from boot until the first
    // connection and active from then on, so calling that state "stopped"
    // invents an SSH outage on every host of that family at every reboot — and
    // the panel's alerting would mail an operator about each one.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", LISTENING);

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_ne!(
        state_of(&statuses, "ssh"),
        ServiceState::Stopped,
        "a healthy socket-activated host was reported as an outage"
    );
    assert_eq!(state_of(&statuses, "ssh"), ServiceState::Unknown);
}

#[test]
fn a_unit_waiting_behind_its_socket_says_so() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", LISTENING);

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");
    let detail = &statuses
        .iter()
        .find(|status| status.unit == "ssh")
        .expect("ssh is in the managed set")
        .detail;

    assert!(
        detail.contains("ssh.socket"),
        "the operator is not told what is standing in for it: {detail}"
    );
}

#[test]
fn a_socket_activated_unit_whose_socket_is_not_listening_is_stopped() {
    // Nothing is listening and the service is not running: that is a real
    // outage, and the same code says so.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", DEAD);

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "ssh"), ServiceState::Stopped);
}

#[test]
fn a_unit_triggered_only_by_a_timer_is_stopped_when_it_is_inactive() {
    // A timer is not standing in for the service the way a socket is: nothing
    // is listening, so an inactive unit really is down.
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit(
        "cron",
        "LoadState=loaded\nActiveState=inactive\nSubState=dead\nTriggeredBy=nightly.timer",
    );

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "cron"), ServiceState::Stopped);
}

#[test]
fn the_rhel_family_reports_its_ssh_service_directly() {
    // AlmaLinux enables `sshd.service` in multi-user.target.wants and does not
    // enable its socket at all, so it has no such window — and the same code
    // answers it without knowing which family it is on.
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit("sshd", RUNNING);

    let statuses = get_service_statuses(&host, rhel_distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "sshd"), ServiceState::Running);
}

#[test]
fn a_unit_that_is_not_installed_is_not_an_outage() {
    // A host without a database server is a host the panel cannot report a
    // database outage on. `systemctl show` says so by exiting zero with
    // LoadState=not-found, which is what makes this an answer.
    let host = FakeMonitorHost::from_ubuntu_captures();

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "mariadb"), ServiceState::Unknown);
}

#[test]
fn a_unit_in_transition_is_not_an_outage() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit(
        "nginx",
        "LoadState=loaded\nActiveState=activating\nSubState=start\nTriggeredBy=",
    );

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "nginx"), ServiceState::Unknown);
}

#[test]
fn a_state_this_agent_does_not_recognise_is_not_an_outage() {
    // A word from a future systemd is not evidence that anything is down.
    let host = FakeMonitorHost::from_ubuntu_captures().with_unit(
        "nginx",
        "LoadState=loaded\nActiveState=refreshing\nSubState=new\nTriggeredBy=",
    );

    let statuses = get_service_statuses(&host, distro()).expect("the service manager answers");

    assert_eq!(state_of(&statuses, "nginx"), ServiceState::Unknown);
}

#[test]
fn a_service_manager_that_cannot_be_started_is_an_error() {
    // Not four simultaneous outages: nobody asked.
    let host = FakeMonitorHost::from_ubuntu_captures().with_absent_service_manager();

    assert_eq!(
        get_service_statuses(&host, distro()),
        Err(MonitorError::ServiceManagerUnavailable { code: -1 })
    );
}

#[test]
fn a_service_manager_that_refuses_the_query_is_an_error() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_service_manager_status(4);

    assert_eq!(
        get_service_statuses(&host, distro()),
        Err(MonitorError::ServiceManagerUnavailable { code: 4 })
    );
}

#[test]
fn the_service_manager_is_only_ever_asked_to_report() {
    // This area changes nothing. `show` reports; `start`, `stop` and `restart`
    // are not spelled anywhere in it.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", LISTENING);

    get_service_statuses(&host, distro()).expect("the service manager answers");

    for command in host.commands() {
        assert_eq!(
            command.first().map(String::as_str),
            Some(distro().service_manager())
        );
        assert_eq!(command.get(1).map(String::as_str), Some("show"));
    }
}

#[test]
fn the_unit_name_is_passed_after_an_end_of_options_separator() {
    // The socket name comes out of a unit's own `TriggeredBy` and is checked
    // only for a `.socket` suffix, which `-H.socket` satisfies — and `-H` is
    // systemctl's "talk to this remote host" option. Root-controlled input
    // today; the separator is what stops it being an argument anybody has to
    // keep reasoning about.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", LISTENING);

    get_service_statuses(&host, distro()).expect("the service manager answers");

    for command in host.commands() {
        let separator = command
            .iter()
            .position(|argument| argument == "--")
            .expect("every call ends its options before naming a unit");

        assert_eq!(
            separator,
            command.len() - 2,
            "the unit must be the only argument after the separator: {command:?}"
        );
        assert!(
            command[..separator]
                .iter()
                .all(|argument| !argument.ends_with(".socket")),
            "a unit name appeared where an option was expected: {command:?}"
        );
    }
}

#[test]
fn no_unit_name_but_the_adapters_own_reaches_the_service_manager() {
    // Status reporting accepts no unit name from a caller, so every name here
    // is either one the adapter fixed or a socket that adapter's own unit named
    // as its trigger.
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_unit("ssh", AWAITING_SOCKET)
        .with_unit("ssh.socket", LISTENING);

    get_service_statuses(&host, distro()).expect("the service manager answers");

    for command in host.commands() {
        let unit = command.last().cloned().unwrap_or_default();
        assert!(
            distro().managed_units().contains(&unit.as_str()) || unit.ends_with(".socket"),
            "an unexpected unit name was asked about: {unit}"
        );
    }
}
