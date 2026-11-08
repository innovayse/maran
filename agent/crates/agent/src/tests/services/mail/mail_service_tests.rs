//! The mail service, driven the way the server drives it: through the trait, on a
//! real runtime, against a host that answers without a daemon.
//!
//! These go through `MailServiceImpl` and not through the operations underneath
//! it, because the things worth pinning are the service's own: that a request is
//! revalidated BEFORE the host is touched, that every outcome is an in-band
//! payload, that the status it sends is the one built from the observed host, and
//! that the twelve data-plane rpcs answer a typed refusal.
//!
//! UNOBSERVED HERE: a real enable (rendering, the daemons' own checks, the bound
//! ports) — `ops`' mail suite drives that against its richer fake, and the
//! polygon suite against real daemons.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{AdapterBinary, DistroAdapter, DistroFamily, adapter_for};
use maran_ops::mail::{ListeningSocket, MailDaemon, MailError, MailHost};
use maran_ops::safe_write::model::{ConfigFile, Reload, Validator};
use maran_ops::ssl::{CertificateInstallation, CertificateState};
use tonic::Request;

use crate::proto::mail_service_server::MailService;
use crate::proto::{
    DisableMailRequest, EnableMailRequest, ErrorCode, GetMailStatusRequest, MailObservation,
    MailStatus, ReloadMailTlsRequest, SetMailboxRequest, disable_mail_response,
    enable_mail_response, get_mail_status_response, reload_mail_tls_response, set_mailbox_response,
};
use crate::services::mail::mail_service::MailServiceImpl;

/// The release the service is built for: Ubuntu 24.04.
const RELEASE: &str = "24.04";

/// The adapter every test runs against.
fn distro() -> &'static dyn DistroAdapter {
    adapter_for(DistroFamily::Debian)
}

/// What the test can still read after the host has moved into the service.
struct Probe {
    /// Every program run, as `program arg arg`.
    spawns: Mutex<Vec<String>>,
    /// How many times the host was asked anything at all.
    asked: AtomicUsize,
}

impl Probe {
    /// How many times the host was asked anything.
    fn asked(&self) -> usize {
        self.asked.load(Ordering::SeqCst)
    }

    /// The service-manager verbs run so far.
    fn verbs(&self) -> Vec<String> {
        let prefix = format!("{} ", distro().service_manager().as_str());
        self.spawns
            .lock()
            .unwrap()
            .iter()
            .filter_map(|line| line.strip_prefix(&prefix))
            .map(|rest| rest.split(' ').next().unwrap().to_owned())
            .collect()
    }
}

/// A host whose units are all active or all stopped, that records what it was
/// asked to run, and that never writes a file.
struct StubHost {
    /// Whether the units read active.
    active: AtomicBool,
    /// Whether certificate material is installed for the hostname.
    certificate_present: bool,
    /// What the test reads back.
    probe: Arc<Probe>,
}

impl StubHost {
    /// A host with the stack `active` or stopped and a certificate installed.
    fn new(active: bool) -> Self {
        Self {
            active: AtomicBool::new(active),
            certificate_present: true,
            probe: Arc::new(Probe {
                spawns: Mutex::new(Vec::new()),
                asked: AtomicUsize::new(0),
            }),
        }
    }

    /// The same host without certificate material.
    fn without_certificate(mut self) -> Self {
        self.certificate_present = false;
        self
    }

    /// The handle the test keeps.
    fn probe(&self) -> Arc<Probe> {
        Arc::clone(&self.probe)
    }
}

impl MailHost for StubHost {
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, MailError> {
        self.probe.asked.fetch_add(1, Ordering::SeqCst);
        self.probe.spawns.lock().unwrap().push(format!(
            "{} {}",
            program.as_str(),
            arguments.join(" ")
        ));

        let (status, stdout) = if program.as_str() == distro().service_manager().as_str() {
            match arguments.first().copied() {
                Some("is-active") if self.active.load(Ordering::SeqCst) => (0, ""),
                Some("is-active") => (3, ""),
                Some("disable") => {
                    self.active.store(false, Ordering::SeqCst);
                    (0, "")
                }
                _ => (0, ""),
            }
        } else if program.as_str() == distro().postconf_binary().as_str() {
            match arguments {
                ["-h", "myhostname"] => (0, "mail.example.test\n"),
                ["-h", "smtpd_tls_chain_files"] => (0, "/k, /c\n"),
                ["-h", "mail_version"] => (0, "3.8.6\n"),
                _ => (0, ""),
            }
        } else if program.as_str() == distro().doveadm_binary().as_str() {
            match arguments {
                ["--version"] => (0, "2.3.19.1 (9b53102964)\n"),
                ["pw", "-l"] => (0, "CRYPT ARGON2ID\n"),
                _ => (64, ""),
            }
        } else {
            (0, "")
        };
        Ok(CommandOutcome {
            status,
            stdout: stdout.to_owned(),
            stderr: String::new(),
        })
    }

    fn read_config(&self, _target: &Path) -> Result<Option<String>, MailError> {
        Ok(None)
    }

    fn write_config_set(
        &self,
        _daemon: MailDaemon,
        _unit: &str,
        _files: &[ConfigFile<'_>],
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), MailError> {
        panic!("these tests never reach a configuration write")
    }

    fn write_file_unreloaded(
        &self,
        _daemon: MailDaemon,
        _unit: &str,
        _file: &ConfigFile<'_>,
        _validator: &Validator<'_>,
    ) -> Result<(), MailError> {
        panic!("these tests never reach a configuration write")
    }

    fn path_exists(&self, _path: &Path) -> bool {
        true
    }

    fn listening_sockets(&self) -> Result<Vec<ListeningSocket>, MailError> {
        if !self.active.load(Ordering::SeqCst) {
            return Ok(Vec::new());
        }
        Ok([25, 465, 587, 143, 993]
            .into_iter()
            .map(|port| ListeningSocket {
                port,
                loopback_only: false,
            })
            .collect())
    }

    fn certificate_state(&self, _domain: &Domain) -> Result<CertificateState, MailError> {
        Ok(CertificateState {
            certificate_path: "/etc/maran/certs/mail.example.test/fullchain.pem".to_owned(),
            private_key_path: "/etc/maran/certs/mail.example.test/privkey.pem".to_owned(),
            installation: if self.certificate_present {
                CertificateInstallation::Installed
            } else {
                CertificateInstallation::Absent
            },
        })
    }

    fn pause(&self, _duration: Duration) {}

    fn monotonic_now(&self) -> Duration {
        Duration::ZERO
    }
}

fn service(host: StubHost) -> MailServiceImpl<StubHost> {
    MailServiceImpl::new(host, distro(), RELEASE)
}

fn enable_request() -> EnableMailRequest {
    EnableMailRequest {
        hostname: "mail.example.test".to_owned(),
        message_size_mib: 50,
        spam_greylist_score: 4,
        spam_add_header_score: 6,
        spam_reject_score: 15,
        ..EnableMailRequest::default()
    }
}

fn status_of(response: get_mail_status_response::Result) -> MailStatus {
    match response {
        get_mail_status_response::Result::Ok(ok) => ok.status.unwrap(),
        get_mail_status_response::Result::Error(error) => panic!("an error: {error:?}"),
    }
}

#[tokio::test]
async fn the_status_of_a_stopped_stack_is_a_state_and_not_an_error() {
    let service = service(StubHost::new(false));

    let response = service
        .get_mail_status(Request::new(GetMailStatusRequest {}))
        .await
        .unwrap()
        .into_inner();
    let status = status_of(response.result.unwrap());

    assert_eq!(status.daemons.len(), 4);
    assert!(
        status.daemons.iter().all(
            |daemon| daemon.active == MailObservation::No as i32 && daemon.listeners.is_empty()
        )
    );
    assert_eq!(status.enabled, MailObservation::No as i32);
    assert!(status.queue.is_none());
}

#[tokio::test]
async fn the_status_of_a_running_stack_carries_what_the_host_shows() {
    let service = service(StubHost::new(true));

    let response = service
        .get_mail_status(Request::new(GetMailStatusRequest {}))
        .await
        .unwrap()
        .into_inner();
    let status = status_of(response.result.unwrap());

    assert_eq!(status.enabled, MailObservation::Yes as i32);
    assert_eq!(status.effective_hostname, "mail.example.test");
    assert_eq!((status.dovecot_major, status.dovecot_minor), (2, 3));
    assert_eq!(status.password_scheme_verified, MailObservation::Yes as i32);
    let mta_ports: Vec<u32> = status.daemons[0]
        .listeners
        .iter()
        .map(|listener| listener.port)
        .collect();
    assert_eq!(mta_ports, [25, 465, 587]);
}

#[tokio::test]
async fn an_unknown_release_is_a_typed_error_in_the_payload() {
    let service = MailServiceImpl::new(StubHost::new(false), distro(), "no-such-release");

    let response = service
        .get_mail_status(Request::new(GetMailStatusRequest {}))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        get_mail_status_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::NotImplemented as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
}

#[tokio::test]
async fn an_invalid_enable_request_is_refused_before_the_host_is_asked_anything() {
    let host = StubHost::new(false);
    let probe = host.probe();
    let service = service(host);
    let mut spoiled = enable_request();
    spoiled.hostname = "mail.example.test\nevil".to_owned();

    let response = service
        .enable_mail(Request::new(spoiled))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        enable_mail_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
    assert_eq!(probe.asked(), 0);
}

#[tokio::test]
async fn a_valid_enable_without_certificate_material_is_not_found_and_changes_nothing() {
    let host = StubHost::new(false).without_certificate();
    let probe = host.probe();
    let service = service(host);

    let response = service
        .enable_mail(Request::new(enable_request()))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        enable_mail_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::NotFound as i32);
            assert!(error.tool_output.is_empty());
        }
        other => panic!("expected an error, got {other:?}"),
    }
    let started = probe
        .verbs()
        .iter()
        .any(|verb| verb == "unmask" || verb == "reload-or-restart");
    assert!(!started);
}

#[tokio::test]
async fn disabling_a_running_stack_answers_the_stack_as_observed_afterwards() {
    let service = service(StubHost::new(true));

    let response = service
        .disable_mail(Request::new(DisableMailRequest {}))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        disable_mail_response::Result::Ok(ok) => {
            let status = ok.status.unwrap();
            assert_eq!(status.enabled, MailObservation::No as i32);
            assert!(
                status
                    .daemons
                    .iter()
                    .all(|daemon| daemon.listeners.is_empty())
            );
        }
        other => panic!("expected ok, got {other:?}"),
    }
}

#[tokio::test]
async fn reloading_the_certificate_of_a_stopped_stack_reloads_nothing() {
    let host = StubHost::new(false);
    let probe = host.probe();
    let service = service(host);

    let response = service
        .reload_mail_tls(Request::new(ReloadMailTlsRequest {}))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        reload_mail_tls_response::Result::Ok(ok) => {
            assert_eq!(ok.status.unwrap().enabled, MailObservation::No as i32);
        }
        other => panic!("expected ok, got {other:?}"),
    }
    assert!(!probe.verbs().iter().any(|verb| verb == "reload-or-restart"));
}

#[tokio::test]
async fn a_data_plane_rpc_answers_a_typed_not_implemented_in_its_payload() {
    let service = service(StubHost::new(false));

    let response = service
        .set_mailbox(Request::new(SetMailboxRequest::default()))
        .await
        .unwrap()
        .into_inner();

    match response.result.unwrap() {
        set_mailbox_response::Result::Error(error) => {
            assert_eq!(error.code, ErrorCode::NotImplemented as i32);
        }
        other => panic!("expected an error, got {other:?}"),
    }
}
