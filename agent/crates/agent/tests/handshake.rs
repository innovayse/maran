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
use maran_agent::proto::accounts_service_client::AccountsServiceClient;
use maran_agent::proto::cron_service_client::CronServiceClient;
use maran_agent::proto::db_service_client::DbServiceClient;
use maran_agent::proto::files_service_client::FilesServiceClient;
use maran_agent::proto::ftps_service_client::FtpsServiceClient;
use maran_agent::proto::php_service_client::PhpServiceClient;
use maran_agent::proto::sftp_service_client::SftpServiceClient;
use maran_agent::proto::sites_service_client::SitesServiceClient;
use maran_agent::proto::ssl_service_client::SslServiceClient;
use maran_agent::proto::system_service_client::SystemServiceClient;
use maran_agent::proto::{
    CreateAccountRequest, CreateDatabaseRequest, CreateDirectoryRequest, CreateFtpsUserRequest,
    CreateSftpUserRequest, CreateSiteRequest, DeleteEntryRequest, DeleteFtpsUserRequest,
    EnableFtpsRequest, EnableFtpsResponse, ErrorCode, GetAccountSuspensionStateRequest,
    GetAccountUsageRequest, GetAgentInfoRequest, GetCronEnvironmentRequest, GetFtpsStatusRequest,
    InstallCertificateRequest, InstallPhpVersionRequest, ListCronEntriesRequest,
    ListDatabasesRequest, ListPhpVersionsRequest, SetFtpsPasswordRequest, SiteBackendType,
    create_account_response, create_database_response, create_ftps_user_response,
    create_sftp_user_response, create_site_response, delete_entry_response,
    delete_ftps_user_response, enable_ftps_response, get_account_suspension_state_response,
    get_account_usage_response, get_agent_info_response, get_cron_environment_response,
    get_ftps_status_response, list_cron_entries_response, set_ftps_password_response,
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

    assert_eq!(info.proto_version, 2);
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

    // The two services that mint credentials. An unguarded registration here
    // would let any local process create a database user or an SFTP login on
    // the host, so they are checked through the rpc that CREATES rather than
    // through a listing: a service that answered this at all would be answering
    // the worst request it takes.
    let database = DbServiceClient::new(channel.clone())
        .create_database(CreateDatabaseRequest::default())
        .await;
    assert_denied("DbService", database.err());

    let login = SftpServiceClient::new(channel.clone())
        .create_sftp_user(CreateSftpUserRequest::default())
        .await;
    assert_denied("SftpService", login.err());

    // The third credential-minting service, checked the same way and through
    // the same worst request it takes: an unguarded registration would let any
    // local process create an FTPS login on the host.
    let ftps = FtpsServiceClient::new(channel.clone())
        .create_ftps_user(CreateFtpsUserRequest::default())
        .await;
    assert_denied("FtpsService", ftps.err());

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

#[tokio::test]
async fn the_database_and_sftp_services_answer_over_the_wire_with_a_typed_refusal() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the database and sftp service test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // An empty account name is the one refusal these services can produce on
    // any machine: it is decided before a database client or a shadow-suite
    // tool is spawned, so the answer is the same whether or not this host has
    // MariaDB or an sshd. Each call proves three things at once — the service
    // is registered, the client-facing contract matches, and a refusal comes
    // back as a typed payload rather than as the transport's UNIMPLEMENTED,
    // which a panel could not tell from "the agent is too old".
    let database = DbServiceClient::new(channel.clone())
        .create_database(CreateDatabaseRequest {
            account_username: String::new(),
            database_name: "shop".to_owned(),
            db_username: "shop".to_owned(),
            password: "Str0ng-pass.word".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();

    match database.result {
        Some(create_database_response::Result::Error(error)) => {
            assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "an unnamed account must be refused, not acted on"
            );
            assert!(
                !error.message.contains("Str0ng-pass.word"),
                "a refusal must never echo the password: {}",
                error.message
            );
        }
        other => panic!("DbService must answer with a typed refusal, got {other:?}"),
    }

    let login = SftpServiceClient::new(channel.clone())
        .create_sftp_user(CreateSftpUserRequest {
            account_username: String::new(),
            sftp_username: "web".to_owned(),
            password: "Str0ng-pass.word".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();

    match login.result {
        Some(create_sftp_user_response::Result::Error(error)) => {
            assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "an unnamed account must be refused, not acted on"
            );
            assert!(
                !error.message.contains("Str0ng-pass.word"),
                "a refusal must never echo the password: {}",
                error.message
            );
        }
        other => panic!("SftpService must answer with a typed refusal, got {other:?}"),
    }

    // And the listing, which is the rpc a panel calls first: an unregistered
    // service answers UNIMPLEMENTED, which this assertion tells apart from a
    // refusal by insisting on an ok response envelope.
    let listing = DbServiceClient::new(channel)
        .list_databases(ListDatabasesRequest {
            account_username: String::new(),
        })
        .await;
    assert!(
        listing.is_ok(),
        "DbService.ListDatabases must be registered, got {:?}",
        listing.err()
    );

    server.abort();
}

/// The agent half of a panel request that names one host twice: refused over the wire as invalid
/// input, before any host access, so the test needs no root and no nginx and is valid on any machine.
///
/// The twin on the panel side treats `ALREADY_EXISTS` as "another vhost serves a name", which is a
/// different answer: this one is the caller's own mistake, and mapping it to `ALREADY_EXISTS` would
/// tell the panel to report a taken domain for a request that can never succeed.
#[tokio::test]
async fn a_create_site_naming_one_host_twice_is_refused_over_the_wire_as_invalid_input() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the repeated host name test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // The alias differs from the domain only in case, which proves the comparison runs after the
    // domain type has lower-cased both: a byte-wise comparison of the raw strings would let it through.
    let response = SitesServiceClient::new(channel)
        .create_site(CreateSiteRequest {
            account_username: "polyrep".to_owned(),
            domain: "rep.test".to_owned(),
            aliases: vec!["REP.test".to_owned()],
            backend_type: SiteBackendType::Static as i32,
            ..CreateSiteRequest::default()
        })
        .await
        .unwrap()
        .into_inner();

    match response.result {
        Some(create_site_response::Result::Error(error)) => {
            assert_eq!(
                error.code,
                ErrorCode::InvalidInput as i32,
                "a request repeating a host name is the caller's mistake, not a taken name: {}",
                error.message
            );
        }
        other => panic!("CreateSite must answer with a typed refusal, got {other:?}"),
    }

    server.abort();
}

#[tokio::test]
async fn the_ftps_service_answers_its_own_typed_errors_rather_than_unimplemented() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the ftps service test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // Every request below is one the agent refuses on its INPUT, before it
    // touches the host — so this test needs no root, mutates nothing, and
    // cannot depend on whether an FTPS daemon happens to be installed on the
    // machine running it. What it proves is that the service is registered and
    // that each rpc is wired to a handler: an rpc the server did not implement
    // answers with the transport's UNIMPLEMENTED status, and an rpc that is
    // implemented answers Ok with a typed error in its payload.
    let enable = FtpsServiceClient::new(channel.clone())
        .enable_ftps(EnableFtpsRequest::default())
        .await
        .unwrap()
        .into_inner();
    match enable.result {
        Some(enable_ftps_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "an empty hostname must be refused as input"
        ),
        other => panic!("EnableFtps must refuse an empty hostname, got {other:?}"),
    }

    let status = FtpsServiceClient::new(channel.clone())
        .get_ftps_status(GetFtpsStatusRequest {
            hostname: "not a hostname".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    match status.result {
        Some(get_ftps_status_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "a hostname the agent will not accept must be refused"
        ),
        other => panic!("GetFtpsStatus must refuse a bad hostname, got {other:?}"),
    }

    let created = FtpsServiceClient::new(channel.clone())
        .create_ftps_user(CreateFtpsUserRequest::default())
        .await
        .unwrap()
        .into_inner();
    match created.result {
        Some(create_ftps_user_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "an empty account name must be refused"
        ),
        other => panic!("CreateFtpsUser must refuse an empty request, got {other:?}"),
    }

    let repassworded = FtpsServiceClient::new(channel.clone())
        .set_ftps_password(SetFtpsPasswordRequest::default())
        .await
        .unwrap()
        .into_inner();
    match repassworded.result {
        Some(set_ftps_password_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "an empty password is refused rather than treated as 'leave unchanged'"
        ),
        other => panic!("SetFtpsPassword must refuse an empty request, got {other:?}"),
    }

    let deleted = FtpsServiceClient::new(channel)
        .delete_ftps_user(DeleteFtpsUserRequest::default())
        .await
        .unwrap()
        .into_inner();
    match deleted.result {
        Some(delete_ftps_user_response::Result::Error(error)) => assert_eq!(
            error.code,
            ErrorCode::InvalidInput as i32,
            "an empty account name must be refused"
        ),
        other => panic!("DeleteFtpsUser must refuse an empty request, got {other:?}"),
    }

    // UNOBSERVED HERE: DisableFtps and ReloadFtpsTls. Neither takes an input
    // this test could get refused — their requests are empty messages — so the
    // only way to reach them is to let them run, and both drive the host's
    // service manager against the real FTPS unit. On a machine where that unit
    // exists and is running, DisableFtps would stop it and take it out of the
    // boot sequence, and ReloadFtpsTls would restart it and abort transfers in
    // flight. A handshake test must not be able to do that to the machine it
    // runs on. They are reachable by construction — the generated server routes
    // all seven rpcs of the service registered above, and this test proves the
    // service IS registered — and they are exercised for real against a real
    // daemon in `ftps_on_a_real_host.rs`.
    eprintln!(
        "UNOBSERVED HERE: FtpsService.DisableFtps and FtpsService.ReloadFtpsTls, which have no \
         refusable input and would drive this host's service manager"
    );

    server.abort();
}

/// The lowest passive port the PANEL puts on the wire.
///
/// A literal, and deliberately not a value read from anywhere: it is the other
/// half of a cross-language pin. The panel's own half is
/// `FtpsDefaults.PassivePortMin` in
/// `backend/src/Maran.Modules/Ftp/Domain/Policies/FtpsDefaults.cs`, pinned to
/// this same literal by `PanelToAgentFtpsWireShapeTests` in
/// `backend/tests/Maran.Host.IntegrationTests/`. Nothing in either language can
/// read the other, so the agreement is held by two literals that name each
/// other: moving one turns the other's test red and the reader is told where the
/// twin is.
const PANEL_PASSIVE_PORT_MIN: u32 = 30_000;

/// The highest passive port the panel puts on the wire. Same pin.
const PANEL_PASSIVE_PORT_MAX: u32 = 30_099;

/// The concurrent-session ceiling the panel puts on the wire. Same pin.
const PANEL_MAX_CLIENTS: u32 = 100;

/// What the panel sends as `passive_address` for a host that is NOT behind NAT.
///
/// The empty string, which the contract defines as "do not write the key at
/// all". This is the one value in the whole shape that an operator can leave
/// blank, and it is therefore the value most hosts send: an agent that refused
/// it would make FTPS unswitchable on every host without a NAT address, while
/// the panel's own validator — which applies its address rule only `When` the
/// field is non-empty — accepted the request and reported the refusal as a
/// server failure.
const PANEL_PASSIVE_ADDRESS_WHEN_NOT_BEHIND_NAT: &str = "";

/// A hostname whose certificate store cannot be populated on any machine.
///
/// `enable_ftps` asks for certificate material FIRST and returns
/// `CertificateMissing` with nothing written, so a hostname nothing can have
/// material for is what makes this test safe to run anywhere: it reaches the
/// operation and stops inside its first step, mutating nothing. `.test` is
/// reserved by RFC 2606 and no certificate authority issues for it. It is NOT
/// `.invalid`: that suffix is in the agent's reserved host name set, so
/// `enable_ftps` refuses it as input before it ever asks for material.
const A_HOSTNAME_NO_CERTIFICATE_STORE_HOLDS: &str = "panel-seam-check.example.test";

/// The request the PANEL builds when an operator enables FTPS on an ordinary
/// host, as `AgentFtpsClient.EnableAsync` puts it on the wire.
fn the_shape_the_panel_sends() -> EnableFtpsRequest {
    EnableFtpsRequest {
        hostname: A_HOSTNAME_NO_CERTIFICATE_STORE_HOLDS.to_owned(),
        passive_port_min: PANEL_PASSIVE_PORT_MIN,
        passive_port_max: PANEL_PASSIVE_PORT_MAX,
        passive_address: PANEL_PASSIVE_ADDRESS_WHEN_NOT_BEHIND_NAT.to_owned(),
        max_clients: PANEL_MAX_CLIENTS,
    }
}

/// The error code an `EnableFtps` response carries, or a panic naming what came
/// back instead.
///
/// # Panics
///
/// Panics when the response is an `Ok` envelope or carries no result at all.
/// Both are failures of this test's premise rather than outcomes it tolerates:
/// an `Ok` would mean the agent had configured and started a daemon for a
/// hostname whose certificate store is empty, which the operation is documented
/// to refuse before it writes anything.
fn refusal_code(response: EnableFtpsResponse) -> i32 {
    match response.result {
        Some(enable_ftps_response::Result::Error(error)) => error.code,
        other => panic!(
            "EnableFtps must answer with a typed refusal for a hostname with no certificate \
             material, got {other:?}"
        ),
    }
}

#[tokio::test]
async fn the_enable_shape_the_panel_sends_passes_the_agents_input_boundary_and_a_wrong_family_passive_address_does_not()
 {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the panel-shape test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;

    // THE SEAM. The panel's half of FTPS is proven against a stub agent and the
    // daemon's half against `ops::ftps` called directly, so the thing neither
    // observes is whether the shape the panel SENDS is a shape the agent
    // ACCEPTS. This asks exactly that, and it asks the real thing: the server
    // started above is `maran_agent::server::serve`, the same code `main` runs,
    // so nothing here can be a stub standing in for the agent.
    //
    // The observation is the SPECIFIC code NOT_FOUND, not "some error". That is
    // the certificate probe's answer, which is the operation's first step — so
    // it can only be reached through `validated_ftps_configuration`, which is
    // the boundary under test. INVALID_INPUT would mean the agent refused the
    // panel's own shape at that boundary; VALIDATION_FAILED or SYSTEM_FAILURE
    // would mean it got further than the probe and started acting on this
    // machine, which this test must never do.
    let enable = FtpsServiceClient::new(channel.clone())
        .enable_ftps(the_shape_the_panel_sends())
        .await
        .unwrap()
        .into_inner();
    assert_eq!(
        refusal_code(enable),
        ErrorCode::NotFound as i32,
        "the agent must accept the panel's own enable shape as INPUT and refuse it only for the \
         missing certificate material: an empty passive_address is the ordinary host, and an agent \
         that refused it would make FTPS unswitchable on every host that is not behind NAT"
    );

    // INVERSE CONTROL ONE, and a real disagreement between the two layers. The
    // assertion above is an ACCEPTANCE, so it is worth nothing unless this
    // boundary still refuses something — and the something is chosen to be a
    // value the PANEL's validator accepts: `EnableFtpsCommandValidator` admits
    // any `IPAddress.TryParse`, IPv6 included, while `pasv_address` is an
    // IPv4-only directive and `PassiveAddress` refuses the wrong family by
    // name. So an operator who types an IPv6 literal is refused HERE and not
    // there. That is today's behaviour on both sides, pinned so that closing the
    // gap on the panel side is a deliberate change rather than an accident.
    let mut wrong_family = the_shape_the_panel_sends();
    wrong_family.passive_address = "2001:db8::1".to_owned();
    let refused = FtpsServiceClient::new(channel.clone())
        .enable_ftps(wrong_family)
        .await
        .unwrap()
        .into_inner();
    assert_eq!(
        refusal_code(refused),
        ErrorCode::InvalidInput as i32,
        "an IPv6 passive address must be refused as INPUT: vsftpd's pasv_address carries four \
         decimal octets and has no IPv6 form"
    );

    // INVERSE CONTROL TWO: a range whose bounds are the wrong way round. The
    // panel cannot send it today — the numbers come from its own constants —
    // but the agent's refusal is its own and not a repetition of the panel's,
    // and a range that renders a daemon serving no passive connection at all is
    // the worst shape a failure can take.
    let mut inverted = the_shape_the_panel_sends();
    inverted.passive_port_min = PANEL_PASSIVE_PORT_MAX;
    inverted.passive_port_max = PANEL_PASSIVE_PORT_MIN;
    let inverted = FtpsServiceClient::new(channel)
        .enable_ftps(inverted)
        .await
        .unwrap()
        .into_inner();
    assert_eq!(
        refusal_code(inverted),
        ErrorCode::InvalidInput as i32,
        "a passive range whose minimum is above its maximum must be refused as INPUT"
    );

    // UNOBSERVED HERE, and it is the larger half of the seam: no daemon is
    // reached by this test and no panel process is either. What it observes is
    // the AGREEMENT between the shape the panel puts on the wire and the shape
    // the agent's input boundary accepts, over a real socket against the real
    // service. Whether a daemon configured from that shape then answers a
    // customer is `ftps_on_a_real_host.rs`, and whether the panel really sends
    // these five values is `EnableFtpsCommandHandlerTests` and
    // `AgentFtpsClientTests` on the other side.
    eprintln!(
        "UNOBSERVED HERE: the daemon. This test reaches the agent's input boundary and the \
         certificate probe behind it, never a running vsftpd and never the panel process."
    );

    server.abort();
}

/// A reserved name that no machine this suite runs on carries as a user, so
/// without the reserved list the agent would answer "no such account" for it.
const RESERVED_NAME_NO_HOST_HOLDS: &str = "ec2_user";

/// A well-formed, unreserved name that no machine this suite runs on carries.
const UNRESERVED_NAME_NO_HOST_HOLDS: &str = "wirecheckacct";

/// Whether the user database of this machine holds `user`.
fn host_has_user(user: &str) -> bool {
    std::process::Command::new("getent")
        .args(["passwd", user])
        .output()
        .unwrap()
        .status
        .success()
}

/// The error code of a `GetAccountUsage` answer, or a panic naming what came
/// back instead.
fn usage_code(response: maran_agent::proto::GetAccountUsageResponse) -> i32 {
    match response.result {
        Some(get_account_usage_response::Result::Error(error)) => error.code,
        other => panic!("GetAccountUsage must answer with a typed refusal, got {other:?}"),
    }
}

/// The error code of a `GetAccountSuspensionState` answer, or a panic.
fn suspension_state_code(response: maran_agent::proto::GetAccountSuspensionStateResponse) -> i32 {
    match response.result {
        Some(get_account_suspension_state_response::Result::Error(error)) => error.code,
        other => {
            panic!("GetAccountSuspensionState must answer with a typed refusal, got {other:?}")
        }
    }
}

/// The error code of a `ListCronEntries` answer, or a panic.
fn cron_list_code(response: maran_agent::proto::ListCronEntriesResponse) -> i32 {
    match response.result {
        Some(list_cron_entries_response::Result::Error(error)) => error.code,
        other => panic!("ListCronEntries must answer with a typed refusal, got {other:?}"),
    }
}

/// The error code of a `GetCronEnvironment` answer, or a panic.
fn cron_environment_code(response: maran_agent::proto::GetCronEnvironmentResponse) -> i32 {
    match response.result {
        Some(get_cron_environment_response::Result::Error(error)) => error.code,
        other => panic!("GetCronEnvironment must answer with a typed refusal, got {other:?}"),
    }
}

/// The error code of a `CreateAccount` answer, or a panic.
fn create_account_code(response: maran_agent::proto::CreateAccountResponse) -> i32 {
    match response.result {
        Some(create_account_response::Result::Error(error)) => error.code,
        other => panic!("CreateAccount must answer with a typed refusal, got {other:?}"),
    }
}

/// Blind spot, stated where it matters: this test returns early on an
/// unsupported host, which would pass silently, and it sends only rpcs that READ
/// (plus a `CreateAccount` for `root`, which stops at a refusal either way). A
/// mutating rpc for a system user is deliberately NOT sent over a real host
/// here, because with the guard removed it would change that user on whatever
/// machine ran the test. Those are settled by the polygon suite
/// `account_identity_on_a_real_host`.
#[tokio::test]
async fn a_reserved_account_name_is_refused_over_the_wire_before_any_tool_runs() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the reserved account name test: {reason}");
            return;
        }
    }

    assert!(
        !host_has_user(RESERVED_NAME_NO_HOST_HOLDS)
            && !host_has_user(UNRESERVED_NAME_NO_HOST_HOLDS),
        "this test needs a machine without a {RESERVED_NAME_NO_HOST_HOLDS} or \
         {UNRESERVED_NAME_NO_HOST_HOLDS} user: with one, the control below proves nothing"
    );

    let channel = connect(&socket_path).await;
    let mut accounts = AccountsServiceClient::new(channel.clone());
    let mut cron = CronServiceClient::new(channel);
    let invalid = ErrorCode::InvalidInput as i32;
    let not_found = ErrorCode::NotFound as i32;

    // `root` is refused by the list AND by the id floor AND by the home rule, so
    // it proves the wiring but not the list; the second name, which only the
    // list refuses, is what proves the list is asked on the shipped path.
    let created = accounts
        .create_account(CreateAccountRequest {
            username: "root".to_owned(),
            quota_bytes: 0,
        })
        .await
        .unwrap()
        .into_inner();
    assert_eq!(
        create_account_code(created),
        invalid,
        "creating `root` must be refused as a reserved name, not reported as already existing"
    );

    for reserved in ["root", RESERVED_NAME_NO_HOST_HOLDS] {
        let usage = accounts
            .get_account_usage(GetAccountUsageRequest {
                username: reserved.to_owned(),
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(usage_code(usage), invalid, "usage of `{reserved}`");

        let state = accounts
            .get_account_suspension_state(GetAccountSuspensionStateRequest {
                username: reserved.to_owned(),
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            suspension_state_code(state),
            invalid,
            "suspension state of `{reserved}`"
        );

        let listing = cron
            .list_cron_entries(ListCronEntriesRequest {
                account_username: reserved.to_owned(),
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            cron_list_code(listing),
            invalid,
            "cron listing of `{reserved}`"
        );

        let environment = cron
            .get_cron_environment(GetCronEnvironmentRequest {
                account_username: reserved.to_owned(),
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            cron_environment_code(environment),
            invalid,
            "cron environment of `{reserved}`"
        );
    }

    // The inverse control: a well-formed, unreserved name the host does not hold
    // is ABSENT, a different answer from a refusal, so the refusals above are
    // not the agent saying no to everything.
    let usage = accounts
        .get_account_usage(GetAccountUsageRequest {
            username: UNRESERVED_NAME_NO_HOST_HOLDS.to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    assert_eq!(usage_code(usage), not_found);

    let listing = cron
        .list_cron_entries(ListCronEntriesRequest {
            account_username: UNRESERVED_NAME_NO_HOST_HOLDS.to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    assert_eq!(cron_list_code(listing), not_found);

    server.abort();
}

/// The error code a `CreateSite` response carries, or a panic naming what came
/// back instead.
///
/// # Panics
///
/// Panics when the response is an `Ok` envelope or carries no result: a reserved
/// name that provisioned a site is the very failure this suite exists to catch.
fn create_site_code(response: maran_agent::proto::CreateSiteResponse) -> i32 {
    match response.result {
        Some(create_site_response::Result::Error(error)) => error.code,
        other => panic!("CreateSite must answer with a typed refusal, got {other:?}"),
    }
}

/// A reserved host name is refused over the wire as invalid input at the doors that
/// write a name into an nginx `server_name` or a certificate store, before any host
/// access, so the test needs no root and no nginx. The panel's own refusal is
/// asserted by the backend suite; this is the agent's own, asked directly with the
/// same names (rules/security.md item 1).
///
/// Blind spot: this proves the doors refuse, not that an ordinary name passes them,
/// since a real ordinary `CreateSite` needs root. The inverse control lives in the
/// pure unit tests of `validated_site`, `validated_identity` and
/// `validated_ftps_configuration`, which accept an ordinary domain and alias.
#[tokio::test]
async fn a_reserved_host_name_is_refused_over_the_wire_as_invalid_input_at_every_door() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the reserved host name test: {reason}");
            return;
        }
    }

    let channel = connect(&socket_path).await;
    let invalid = ErrorCode::InvalidInput as i32;
    let reserved = [
        "203.0.113.7",
        "shop.123",
        "foo.localhost",
        "x.local",
        "x.localdomain",
        "x.internal",
        "x.lan",
        "x.invalid",
        "FOO.LOCAL",
    ];

    for name in reserved {
        // The primary domain.
        let as_domain = SitesServiceClient::new(channel.clone())
            .create_site(CreateSiteRequest {
                account_username: "polyrsv".to_owned(),
                domain: name.to_owned(),
                backend_type: SiteBackendType::Static as i32,
                ..CreateSiteRequest::default()
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            create_site_code(as_domain),
            invalid,
            "a site whose domain is `{name}` must be refused as input"
        );

        // An alias, beside an ordinary primary domain.
        let as_alias = SitesServiceClient::new(channel.clone())
            .create_site(CreateSiteRequest {
                account_username: "polyrsv".to_owned(),
                domain: "rsv.example.test".to_owned(),
                aliases: vec![name.to_owned()],
                backend_type: SiteBackendType::Static as i32,
                ..CreateSiteRequest::default()
            })
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            create_site_code(as_alias),
            invalid,
            "a site with the alias `{name}` must be refused as input"
        );

        // The FTPS server's host name.
        let mut enable = the_shape_the_panel_sends();
        enable.hostname = name.to_owned();
        let enabled = FtpsServiceClient::new(channel.clone())
            .enable_ftps(enable)
            .await
            .unwrap()
            .into_inner();
        assert_eq!(
            refusal_code(enabled),
            invalid,
            "FTPS on the host name `{name}` must be refused as input"
        );
    }

    // The inverse control, read-only: asking for the status of an FTPS host name that was
    // reserved before this rule existed is NOT refused as input, because read-only
    // rpcs keep working for names already on the host.
    let status = FtpsServiceClient::new(channel)
        .get_ftps_status(GetFtpsStatusRequest {
            hostname: "legacy.internal".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    if let Some(get_ftps_status_response::Result::Error(error)) = status.result {
        assert_ne!(
            error.code, invalid,
            "a read-only status query for an existing reserved name must not be refused as input"
        );
    }

    server.abort();
}
