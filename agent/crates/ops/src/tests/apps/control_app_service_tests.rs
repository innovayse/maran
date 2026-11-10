//! Tests for [`control_app_service`]: one fixed subcommand per action, refusals for what cannot
//! be controlled.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::apps::validated_app_id::ValidatedAppId;

use super::control_app_service;
use crate::apps::{AppServiceAction, AppServiceStatus, AppsOpError};
use crate::php::fake_php_host::{FakePhpHost, distro};

fn id(text: &str) -> ValidatedAppId {
    ValidatedAppId::parse(text).unwrap()
}

fn host_with_apache() -> FakePhpHost {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/apache2ctl"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "apache\n");
    host
}

#[test]
fn each_action_is_one_fixed_systemctl_subcommand_on_the_apps_unit() {
    for (action, word) in [
        (AppServiceAction::Start, "start"),
        (AppServiceAction::Stop, "stop"),
        (AppServiceAction::Restart, "restart"),
        (AppServiceAction::Reload, "reload"),
    ] {
        let host = host_with_apache();
        host.answer("is-active", 0, "active\n", "");

        let status = control_app_service(&host, distro(), &id("apache"), action).unwrap();

        assert_eq!(status, AppServiceStatus::Active);
        let call = host
            .commands()
            .into_iter()
            .find(|call| call.get(1).map(String::as_str) == Some(word))
            .unwrap();
        assert_eq!(call[2..], ["--", "apache2"]);
    }
}

#[test]
fn an_app_that_is_not_installed_cannot_be_controlled() {
    let host = FakePhpHost::empty();

    assert!(matches!(
        control_app_service(&host, distro(), &id("apache"), AppServiceAction::Start),
        Err(AppsOpError::NotInstalled { .. })
    ));
    assert!(host.commands().is_empty());
}

#[test]
fn nodejs_has_no_service_to_control() {
    let host = FakePhpHost::empty();
    host.answer("--version", 0, "v20.11.1\n", "");

    assert!(matches!(
        control_app_service(&host, distro(), &id("nodejs-20"), AppServiceAction::Restart),
        Err(AppsOpError::ActionRefused { .. })
    ));
}

#[test]
fn nginx_cannot_be_stopped_because_it_is_the_panels_front_but_can_be_reloaded() {
    let host = FakePhpHost::empty();
    host.place_config(Path::new("/usr/sbin/nginx"), "");
    host.place_config(Path::new("/etc/maran/site-web-server"), "nginx\n");

    assert!(matches!(
        control_app_service(&host, distro(), &id("nginx"), AppServiceAction::Stop),
        Err(AppsOpError::ActionRefused { .. })
    ));
    assert!(
        host.commands()
            .iter()
            .all(|call| call.get(1).map(String::as_str) != Some("stop"))
    );

    // Inverse control: the same app, a different action, goes through.
    control_app_service(&host, distro(), &id("nginx"), AppServiceAction::Reload).unwrap();
}

#[test]
fn a_service_manager_refusal_carries_its_words() {
    let host = host_with_apache();
    host.answer("reload", 1, "", "Job for apache2.service failed");

    match control_app_service(&host, distro(), &id("apache"), AppServiceAction::Reload) {
        Err(AppsOpError::ServiceManager { stderr }) => {
            assert!(stderr.contains("Job for apache2.service failed"));
        }
        other => panic!("expected ServiceManager, got {other:?}"),
    }
}
