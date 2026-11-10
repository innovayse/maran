//! `AppsServiceImpl`, driven the way the server drives it: the stream shape of an install and
//! the typed refusals, against a host that records instead of installing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeSet;
use std::path::Path;
use std::sync::{Arc, Mutex};

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::panel_host_names::PanelHostNames;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};
use maran_ops::apps::AppsHost;
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{ConfigHost, SafeWriteError};
use tokio_stream::StreamExt;
use tonic::Request;

use crate::proto::apps_service_server::AppsService;
use crate::proto::{
    ControlAppServiceRequest, ErrorCode, InstallAppRequest, ListAppsRequest, UninstallAppRequest,
    control_app_service_response, install_app_response, list_apps_response, uninstall_app_response,
};
use crate::services::apps::apps_service::AppsServiceImpl;

fn adapter() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// A host with nothing installed on which `start` succeeds and leaves Node.js 18 behind.
struct RecordingHost {
    /// Whether the Node.js interpreter exists yet.
    node_installed: Mutex<bool>,
    /// Every argv handed to `run`.
    calls: Arc<Mutex<Vec<Vec<String>>>>,
}

impl ConfigHost for RecordingHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_string()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        let mut outcome = CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        };
        match arguments.first().copied() {
            Some("start") => *self.node_installed.lock().unwrap() = true,
            Some("--version") => {
                if *self.node_installed.lock().unwrap() {
                    outcome.stdout = "v18.19.1\n".to_owned();
                } else {
                    outcome.status = 127;
                }
            }
            _ => {}
        }
        Ok(outcome)
    }
}

impl PhpHost for RecordingHost {
    fn directory_exists(&self, _path: &Path) -> bool {
        false
    }

    fn read_config(&self, _path: &Path) -> Option<String> {
        None
    }

    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        panic!("a Node.js install creates no directory")
    }

    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        panic!("an app install creates nothing as an account")
    }

    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("a Node.js install writes no configuration")
    }

    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an app install removes no file")
    }

    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        panic!("an app install reloads nothing")
    }
}

impl AppsHost for RecordingHost {
    fn path_exists(&self, _path: &Path) -> bool {
        false
    }

    fn directory_entries(&self, _path: &Path) -> Vec<String> {
        Vec::new()
    }

    fn write_record(
        &self,
        _path: &Path,
        _contents: &str,
    ) -> Result<(), maran_ops::apps::AppsOpError> {
        Ok(())
    }

    fn remove_record(&self, _path: &Path) -> Result<(), maran_ops::apps::AppsOpError> {
        Ok(())
    }
}

fn service(calls: &Arc<Mutex<Vec<Vec<String>>>>) -> AppsServiceImpl<RecordingHost> {
    AppsServiceImpl::new(
        RecordingHost {
            node_installed: Mutex::new(false),
            calls: Arc::clone(calls),
        },
        adapter(),
        PanelHostNames::none(),
    )
}

#[tokio::test(flavor = "multi_thread")]
async fn an_install_streams_progress_and_ends_with_exactly_one_ok() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let response = service(&calls)
        .install_app(Request::new(InstallAppRequest {
            app_id: "nodejs-18".to_owned(),
        }))
        .await
        .unwrap();

    let messages: Vec<_> = response.into_inner().collect().await;
    let results: Vec<_> = messages
        .into_iter()
        .map(|message| message.unwrap().result.unwrap())
        .collect();

    let (terminal, progress) = results.split_last().unwrap();
    assert!(!progress.is_empty());
    assert!(
        progress
            .iter()
            .all(|result| matches!(result, install_app_response::Result::Progress(_)))
    );
    match terminal {
        install_app_response::Result::Ok(ok) => assert_eq!(ok.app_id, "nodejs-18"),
        other => panic!("expected the ok terminal, got {other:?}"),
    }
    let started: BTreeSet<String> = calls
        .lock()
        .unwrap()
        .iter()
        .filter(|call| call.get(1).map(String::as_str) == Some("start"))
        .map(|call| call.last().unwrap().clone())
        .collect();
    assert_eq!(
        started,
        BTreeSet::from(["maran-app-install@nodejs.service".to_owned()])
    );
}

#[tokio::test(flavor = "multi_thread")]
async fn an_id_outside_the_catalogue_is_one_invalid_input_message_and_no_command() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let response = service(&calls)
        .install_app(Request::new(InstallAppRequest {
            app_id: "apache2".to_owned(),
        }))
        .await
        .unwrap();

    let messages: Vec<_> = response.into_inner().collect().await;

    assert_eq!(messages.len(), 1);
    match messages
        .into_iter()
        .next()
        .unwrap()
        .unwrap()
        .result
        .unwrap()
    {
        install_app_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
    assert!(calls.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn removing_an_app_that_is_not_installed_is_app_not_installed() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let response = service(&calls)
        .uninstall_app(Request::new(UninstallAppRequest {
            app_id: "nodejs-20".to_owned(),
        }))
        .await
        .unwrap();

    let messages: Vec<_> = response.into_inner().collect().await;

    match messages
        .into_iter()
        .last()
        .unwrap()
        .unwrap()
        .result
        .unwrap()
    {
        uninstall_app_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::AppNotInstalled as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
}

#[tokio::test(flavor = "multi_thread")]
async fn a_request_with_no_action_is_invalid_input_and_runs_nothing() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let response = service(&calls)
        .control_app_service(Request::new(ControlAppServiceRequest {
            app_id: "apache".to_owned(),
            action: 0,
        }))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        control_app_service_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
    assert!(calls.lock().unwrap().is_empty());
}

#[tokio::test(flavor = "multi_thread")]
async fn a_host_with_nothing_installed_lists_no_apps() {
    let calls = Arc::new(Mutex::new(Vec::new()));
    let response = service(&calls)
        .list_apps(Request::new(ListAppsRequest {}))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        list_apps_response::Result::Ok(ok) => assert!(ok.apps.is_empty()),
        other => panic!("expected ok, got {other:?}"),
    }
}
