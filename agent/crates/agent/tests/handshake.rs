//! The handshake over a real unix socket: a server started in-process is
//! answered by the generated client, so the proto contract, the codec and the
//! transport are all exercised rather than mocked.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::time::{Duration, Instant};

use hyper_util::rt::TokioIo;
use tokio::net::UnixStream;
use tonic::transport::{Channel, Endpoint, Uri};
use tower::service_fn;

use maran_agent::error::StartupError;
use maran_agent::peercred::PeerPolicy;
use maran_agent::proto::files_service_client::FilesServiceClient;
use maran_agent::proto::php_service_client::PhpServiceClient;
use maran_agent::proto::sites_service_client::SitesServiceClient;
use maran_agent::proto::ssl_service_client::SslServiceClient;
use maran_agent::proto::system_service_client::SystemServiceClient;
use maran_agent::proto::{
    CreateDirectoryRequest, CreateSiteRequest, DeleteEntryRequest, ErrorCode, GetAgentInfoRequest,
    InstallCertificateRequest, InstallPhpVersionRequest, ListPhpVersionsRequest,
    delete_entry_response, get_agent_info_response,
};

/// How long the test waits for the server to bind before declaring it stuck.
const BIND_TIMEOUT: Duration = Duration::from_secs(5);

/// Gap between two checks for the socket file while the server starts.
const BIND_POLL_INTERVAL: Duration = Duration::from_millis(20);

/// Authority the endpoint is built with. It is never resolved: the custom
/// connector below ignores it and dials the socket path instead, but tonic still
/// requires a syntactically valid URI to build the `:authority` header from.
const UNUSED_AUTHORITY: &str = "http://uds.invalid";

/// What happened while the server was starting.
enum Started {
    /// The socket exists and is ready to be dialled.
    Listening,
    /// The host is outside the supported matrix, so there is nothing to test.
    UnsupportedHost(String),
}

#[tokio::test]
async fn handshake_over_a_unix_socket_reports_the_agent_and_the_host() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the handshake test: {reason}");
            return;
        }
    }

    let mut client = SystemServiceClient::new(connect(&socket_path).await);
    let response = client
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .unwrap()
        .into_inner();

    let info = match response.result {
        Some(get_agent_info_response::Result::Ok(info)) => info,
        other => panic!("the handshake must succeed on a supported host, got {other:?}"),
    };

    assert_eq!(info.proto_version, 1);
    assert!(
        !info.version.is_empty(),
        "the agent must report its version"
    );
    assert_eq!(info.distro_id, host_distro_id());

    server.abort();
}

/// Polls for the socket file until the server binds it, the server gives up, or
/// [`BIND_TIMEOUT`] elapses.
///
/// The socket file appearing is the only observable "ready" signal a bound
/// listener leaves behind, so it is polled rather than waited out with a fixed
/// sleep that would be both slower and racier.
async fn wait_until_listening(
    socket_path: &Path,
    server: &mut tokio::task::JoinHandle<Result<(), StartupError>>,
) -> Started {
    let deadline = Instant::now() + BIND_TIMEOUT;

    loop {
        if socket_path.exists() {
            return Started::Listening;
        }

        if server.is_finished() {
            let outcome = server.await.expect("the server task must not panic");
            return match outcome {
                Err(StartupError::Distro(error)) => Started::UnsupportedHost(error.to_string()),
                other => panic!("the server stopped before binding: {other:?}"),
            };
        }

        assert!(
            Instant::now() < deadline,
            "the server did not bind {} within {BIND_TIMEOUT:?}",
            socket_path.display()
        );

        tokio::time::sleep(BIND_POLL_INTERVAL).await;
    }
}

/// Opens a gRPC channel onto the unix socket at `socket_path`.
async fn connect(socket_path: &Path) -> Channel {
    let path = socket_path.to_path_buf();

    Endpoint::try_from(UNUSED_AUTHORITY)
        .unwrap()
        .connect_with_connector(service_fn(move |_: Uri| {
            let path = path.clone();
            async move { Ok::<_, std::io::Error>(TokioIo::new(UnixStream::connect(path).await?)) }
        }))
        .await
        .unwrap()
}

/// The host's os-release `ID`, read independently of the agent's own detection
/// so the assertion checks the reported value against the system rather than
/// against the code under test.
fn host_distro_id() -> String {
    let content = std::fs::read_to_string("/etc/os-release").unwrap();

    content
        .lines()
        .find_map(|line| line.strip_prefix("ID="))
        .map(|value| value.trim_matches('"').to_owned())
        .expect("a Linux host publishes ID in /etc/os-release")
}

#[tokio::test]
async fn every_new_service_refuses_a_uid_the_policy_does_not_allow() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    // A policy that allows the NEXT uid, so this test process — which is the
    // one that will connect — is exactly the disallowed caller. There is no
    // other way to be a different uid without being root, and a test that had
    // to be root would not run.
    let uid = maran_agent_core::utils::current_uid::current_uid().unwrap();
    let policy = PeerPolicy::new(uid.wrapping_add(1));
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        // A host outside the support matrix cannot bind, so there is no
        // server to refuse anything. Reporting that as a pass would make this
        // test — the only thing standing between a forgotten interceptor and a
        // world-reachable root daemon — green on exactly the machines nobody
        // checks. The reason is asserted to be the one skip that is legitimate,
        // and the skip is printed where CI can see it.
        Started::UnsupportedHost(reason) => {
            assert!(
                reason.contains("unsupported") || reason.contains("Unsupported"),
                "the guard test may only be skipped for an unsupported host, got: {reason}"
            );
            eprintln!(
                "SKIPPED every_new_service_refuses_a_uid_the_policy_does_not_allow: {reason}"
            );
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // Every request below is one the agent would REFUSE on its own inputs if
    // it ever reached a handler — an empty domain, an unsupported version.
    // That is deliberate: if a service were registered without its guard, the
    // call would come back Ok with an INVALID_INPUT payload instead of a
    // PermissionDenied status, so this test fails loudly rather than running a
    // root operation on the machine it is running on.
    let sites = SitesServiceClient::new(channel.clone())
        .create_site(CreateSiteRequest::default())
        .await;
    assert_denied("SitesService", sites.err());

    let ssl = SslServiceClient::new(channel.clone())
        .install_certificate(InstallCertificateRequest::default())
        .await;
    assert_denied("SslService", ssl.err());

    let listing = PhpServiceClient::new(channel.clone())
        .list_php_versions(ListPhpVersionsRequest {})
        .await;
    assert_denied("PhpService.ListPhpVersions", listing.err());

    // The service that reaches into a customer's home is checked here too, and
    // through the rpc it can refuse without root: an unguarded registration
    // would answer with an INVALID_INPUT payload instead of PermissionDenied.
    let files = FilesServiceClient::new(channel.clone())
        .delete_entry(DeleteEntryRequest::default())
        .await;
    assert_denied("FilesService", files.err());

    // The streaming rpc too: the interceptor runs per request, but a service
    // registered without a guard would leak through whichever rpc nobody
    // checked.
    let install = PhpServiceClient::new(channel)
        .install_php_version(InstallPhpVersionRequest {
            version: String::new(),
        })
        .await;
    assert_denied("PhpService.InstallPhpVersion", install.err());

    server.abort();
}

/// Asserts that `outcome` is the guard's refusal and not something else.
///
/// A missing guard shows up here as `None` — the call succeeded — and a
/// different failure shows up as a different code, so neither can pass as a
/// refusal.
fn assert_denied(service: &str, outcome: Option<tonic::Status>) {
    let status =
        outcome.unwrap_or_else(|| panic!("{service} answered a uid the policy does not allow"));

    assert_eq!(
        status.code(),
        tonic::Code::PermissionDenied,
        "{service} must refuse a disallowed uid, got {status:?}"
    );
}

#[tokio::test]
async fn the_files_service_answers_over_the_wire_and_refuses_what_it_does_not_implement() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the files service test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // A recursive removal is the one refusal this test can produce without
    // root: it is decided before any account is resolved, so the answer is the
    // same on every machine. It proves three things at once — the service is
    // registered, the client-facing contract matches, and the flag the agent
    // does not implement is refused rather than silently carried out as a
    // single-file removal.
    let refused = FilesServiceClient::new(channel.clone())
        .delete_entry(DeleteEntryRequest {
            account_username: "acme".to_owned(),
            path: "sites/example.com/.well-known/acme-challenge/token".to_owned(),
            recursive: true,
        })
        .await
        .unwrap()
        .into_inner();

    match refused.result {
        Some(delete_entry_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "a recursive removal must be refused, not performed"
        ),
        other => panic!("recursive removal must be refused, got {other:?}"),
    }

    // An rpc this agent does not implement answers with the transport's own
    // UNIMPLEMENTED rather than an ok payload, so a panel calling it cannot
    // read "nothing happened" as "it worked".
    let unimplemented = FilesServiceClient::new(channel)
        .create_directory(CreateDirectoryRequest {
            account_username: "acme".to_owned(),
            path: "sites".to_owned(),
            mode: 0o755,
        })
        .await;

    assert_eq!(
        unimplemented.err().map(|status| status.code()),
        Some(tonic::Code::Unimplemented),
        "an rpc that is not built must say so"
    );

    server.abort();
}
