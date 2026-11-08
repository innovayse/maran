//! The closing test of the audit trail (issue #106): the real server, on a real unix
//! socket, with a capturing tracing subscriber, answers mutating and read-only rpcs and
//! the captured output is read back the way an operator reads the journal.
//!
//! What this proves and what it cannot: the captured writer is the tracing writer, not
//! journald, so that a line reaches the journal, survives its rate limit and carries the
//! right unit identity is UNOBSERVED HERE (threat note, section 9). Every request below
//! is refused before it reaches the host, so no test needs root or a database.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/captured_log.rs"]
mod captured_log;
#[path = "fixtures/in_process_agent.rs"]
mod in_process_agent;

use std::time::{Duration, Instant};

use maran_agent::proto::accounts_service_client::AccountsServiceClient;
use maran_agent::proto::backup_service_client::BackupServiceClient;
use maran_agent::proto::cron_service_client::CronServiceClient;
use maran_agent::proto::db_service_client::DbServiceClient;
use maran_agent::proto::files_service_client::FilesServiceClient;
use maran_agent::proto::firewall_service_client::FirewallServiceClient;
use maran_agent::proto::php_service_client::PhpServiceClient;
use maran_agent::proto::sftp_service_client::SftpServiceClient;
use maran_agent::proto::sites_service_client::SitesServiceClient;
use maran_agent::proto::system_service_client::SystemServiceClient;
use maran_agent::proto::{
    AllowPortRequest, BackupDestination, BackupDestinationKind, CreateAccountRequest,
    CreateBackupRequest, CreateCronEntryRequest, CreateSiteRequest, CronSchedule,
    DropDatabaseRequest, ErrorCode, GetAgentInfoRequest, InstallPhpVersionRequest,
    ListCronEntriesRequest, ListDatabasesRequest, Protocol, PruneSiteLogsRequest,
    SetSftpPasswordRequest, SiteBackendType, SummariseSiteTrafficRequest, WriteFileRequest,
    allow_port_response, create_account_response, create_backup_response,
    create_cron_entry_response, create_site_response, drop_database_response,
    install_php_version_response, prune_site_logs_response, set_sftp_password_response,
    write_file_response,
};
use tonic::transport::Channel;

use captured_log::CapturedLog;
use in_process_agent::InProcessAgent;

/// A password no real request carries; it must never reach a captured byte.
const CANARY_PASSWORD: &str = "CANARY-pw-5f1c";

/// A cron command no real request carries; it must never reach a captured byte.
const CANARY_COMMAND: &str = "echo CANARY-cmd-9d2e";

/// A name that passes the account grammar but belongs to no account on any host.
const UNKNOWN_ACCOUNT: &str = "auditprobe";

/// A name the account grammar refuses (capital letters and spaces).
const INVALID_NAME: &str = "Not A Name";

/// How long a test waits for a line it expects, before it calls the line missing.
const LINE_BUDGET: Duration = Duration::from_secs(10);

/// Gap between two looks at the captured output while waiting.
const LINE_POLL_INTERVAL: Duration = Duration::from_millis(10);

/// The proto name of an error code, as the audit line spells it.
fn code_name(code: i32) -> String {
    ErrorCode::try_from(code).unwrap().as_str_name().to_owned()
}

/// The code of the typed refusal in a response, or a failure naming what came instead.
macro_rules! refusal {
    ($response:expr, $module:ident) => {
        match $response.result {
            Some($module::Result::Error(error)) => code_name(error.code),
            other => panic!("the request must be refused, got {other:?}"),
        }
    };
}

/// Waits until the log holds at least `count` audit lines, then returns them all.
///
/// Polling with a budget and no fixed sleep: the line is written when the response body
/// ends, which can be a moment after the client decoded its last message.
async fn audit_lines_after_waiting_for(log: &CapturedLog, count: usize) -> Vec<String> {
    let deadline = Instant::now() + LINE_BUDGET;
    loop {
        let lines: Vec<String> = log
            .lines()
            .into_iter()
            .filter(|line| line.contains("event=rpc"))
            .collect();
        if lines.len() >= count {
            return lines;
        }
        assert!(
            Instant::now() < deadline,
            "expected {count} audit line(s) within {LINE_BUDGET:?}, have {lines:?}"
        );
        tokio::time::sleep(LINE_POLL_INTERVAL).await;
    }
}

/// The value of `key=` in an audit line.
fn field<'a>(line: &'a str, key: &str) -> &'a str {
    let prefix = format!("{key}=");
    line.split_whitespace()
        .find_map(|part| part.strip_prefix(prefix.as_str()))
        .unwrap_or_else(|| panic!("no `{key}=` in `{line}`"))
}

/// Whether `needle` occurs anywhere in `haystack`.
fn contains_bytes(haystack: &[u8], needle: &[u8]) -> bool {
    haystack
        .windows(needle.len())
        .any(|window| window == needle)
}

/// Asserts that the `index`-th audit line is the one for `method` ending with `outcome`.
fn assert_line(lines: &[String], index: usize, method: &str, outcome: &str) {
    let line = &lines[index];
    assert_eq!(field(line, "event"), "rpc", "{line}");
    assert_eq!(field(line, "method"), method, "{line}");
    assert_eq!(field(line, "outcome"), outcome, "{line}");
}

/// A mutating rpc that is answered without reaching the host, used as a barrier: once its
/// line has been read, every earlier request of the test has already had its chance to
/// write one.
async fn refused_create_account(channel: Channel) -> String {
    let response = AccountsServiceClient::new(channel)
        .create_account(CreateAccountRequest {
            username: "root".to_owned(),
            quota_bytes: 0,
            ..Default::default()
        })
        .await
        .unwrap()
        .into_inner();
    refusal!(response, create_account_response)
}

#[tokio::test]
async fn every_refused_mutating_rpc_yields_exactly_one_line_with_its_refusal_code() {
    let log = CapturedLog::start();
    let agent = InProcessAgent::start().await;
    let channel = agent.channel();
    let mut expected: Vec<(&str, String)> = Vec::new();

    expected.push((
        "maran.agent.v1.AccountsService/CreateAccount",
        refused_create_account(channel.clone()).await,
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    let response = SitesServiceClient::new(channel.clone())
        .create_site(CreateSiteRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            domain: "not a host!".to_owned(),
            backend_type: SiteBackendType::Static as i32,
            ..Default::default()
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.SitesService/CreateSite",
        refusal!(response, create_site_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    let response = SftpServiceClient::new(channel.clone())
        .set_sftp_password(SetSftpPasswordRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            sftp_username: "auditprobe_sftp".to_owned(),
            password: CANARY_PASSWORD.to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.SftpService/SetSftpPassword",
        refusal!(response, set_sftp_password_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    // A database name the grammar refuses: dropping an unknown but well-formed one is a
    // success by the idempotency rule, which is not a refusal and not what is asserted.
    let response = DbServiceClient::new(channel.clone())
        .drop_database(DropDatabaseRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            database_name: INVALID_NAME.to_owned(),
            db_username: String::new(),
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.DbService/DropDatabase",
        refusal!(response, drop_database_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    let response = CronServiceClient::new(channel.clone())
        .create_cron_entry(CreateCronEntryRequest {
            account_username: INVALID_NAME.to_owned(),
            schedule: Some(CronSchedule {
                minute: "0".to_owned(),
                hour: "0".to_owned(),
                day_of_month: "*".to_owned(),
                month: "*".to_owned(),
                day_of_week: "*".to_owned(),
            }),
            command: CANARY_COMMAND.to_owned(),
            ..Default::default()
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.CronService/CreateCronEntry",
        refusal!(response, create_cron_entry_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    let response = FirewallServiceClient::new(channel.clone())
        .allow_port(AllowPortRequest {
            port: 0,
            protocol: Protocol::Tcp as i32,
            source_cidr: String::new(),
            ..Default::default()
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.FirewallService/AllowPort",
        refusal!(response, allow_port_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    // A client-streamed write whose first message carries no header.
    let headerless = tokio_stream::iter(vec![WriteFileRequest {
        header: None,
        chunk: b"bytes".to_vec(),
    }]);
    let response = FilesServiceClient::new(channel.clone())
        .write_file(headerless)
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.FilesService/WriteFile",
        refusal!(response, write_file_response),
    ));
    audit_lines_after_waiting_for(&log, expected.len()).await;

    // A retention of zero is what an absent field decodes to, and is refused before the
    // host is touched (the case that would otherwise remove every archive).
    let response = SitesServiceClient::new(channel.clone())
        .prune_site_logs(PruneSiteLogsRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            domain: "example.com".to_owned(),
            retain_rotations: 0,
        })
        .await
        .unwrap()
        .into_inner();
    expected.push((
        "maran.agent.v1.SitesService/PruneSiteLogs",
        refusal!(response, prune_site_logs_response),
    ));

    let lines = audit_lines_after_waiting_for(&log, expected.len()).await;
    assert_eq!(
        lines.len(),
        expected.len(),
        "one line per mutating rpc, no more: {lines:?}"
    );
    for (index, (method, outcome)) in expected.iter().enumerate() {
        assert_line(&lines, index, method, outcome);
        assert_ne!(field(&lines[index], "outcome"), "ok");
        assert_ne!(field(&lines[index], "outcome"), "unset");
    }

    // DropDatabase validated its account before the database name was refused, so its
    // line names the account (SetSftpPassword above is refused as ONE bundle, because
    // "auditprobe_sftp" is outside the login-suffix grammar, so it names none); a field
    // that was never validated is `-`.
    assert_eq!(field(&lines[3], "account"), UNKNOWN_ACCOUNT, "{}", lines[3]);
    assert_eq!(field(&lines[2], "account"), "-", "{}", lines[2]);
    assert_eq!(field(&lines[5], "account"), "-", "{}", lines[5]);
}

#[tokio::test]
async fn read_only_rpcs_yield_no_line() {
    let log = CapturedLog::start();
    let agent = InProcessAgent::start().await;
    let channel = agent.channel();

    SystemServiceClient::new(channel.clone())
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .unwrap();
    DbServiceClient::new(channel.clone())
        .list_databases(ListDatabasesRequest {
            account_username: INVALID_NAME.to_owned(),
        })
        .await
        .unwrap();
    CronServiceClient::new(channel.clone())
        .list_cron_entries(ListCronEntriesRequest {
            account_username: INVALID_NAME.to_owned(),
        })
        .await
        .unwrap();

    SitesServiceClient::new(channel.clone())
        .summarise_site_traffic(SummariseSiteTrafficRequest {
            account_username: INVALID_NAME.to_owned(),
            domain: "example.com".to_owned(),
            cursor: None,
        })
        .await
        .unwrap();

    // Without a mutating rpc after them the requests above could be "no line
    // yet" and look identical to "no line ever". The barrier's own line is written
    // after theirs would have been, so finding exactly one proves they wrote none.
    refused_create_account(channel).await;
    let lines = audit_lines_after_waiting_for(&log, 1).await;
    assert_eq!(lines.len(), 1, "only the barrier may write: {lines:?}");
    assert_eq!(
        field(&lines[0], "method"),
        "maran.agent.v1.AccountsService/CreateAccount"
    );
}

#[tokio::test]
async fn a_canary_in_a_request_reaches_no_captured_byte() {
    let log = CapturedLog::start();
    let agent = InProcessAgent::start().await;
    let channel = agent.channel();

    SftpServiceClient::new(channel.clone())
        .set_sftp_password(SetSftpPasswordRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            sftp_username: "auditprobe_sftp".to_owned(),
            password: CANARY_PASSWORD.to_owned(),
        })
        .await
        .unwrap();
    CronServiceClient::new(channel.clone())
        .create_cron_entry(CreateCronEntryRequest {
            account_username: UNKNOWN_ACCOUNT.to_owned(),
            schedule: Some(CronSchedule {
                minute: "0".to_owned(),
                hour: "0".to_owned(),
                day_of_month: "*".to_owned(),
                month: "*".to_owned(),
                day_of_week: "*".to_owned(),
            }),
            command: CANARY_COMMAND.to_owned(),
            ..Default::default()
        })
        .await
        .unwrap();

    let lines = audit_lines_after_waiting_for(&log, 2).await;
    assert_eq!(lines.len(), 2, "{lines:?}");

    let captured = log.bytes();
    assert!(!captured.is_empty(), "the probe has something to look at");
    for canary in [CANARY_PASSWORD, CANARY_COMMAND, "CANARY-pw", "CANARY-cmd"] {
        assert!(
            !contains_bytes(&captured, canary.as_bytes()),
            "`{canary}` reached the captured output"
        );
    }
}

#[tokio::test]
async fn the_canary_probe_finds_a_canary_that_is_logged_on_the_audit_target() {
    let log = CapturedLog::start();

    tracing::info!(target: "maran_audit", "planted {CANARY_PASSWORD} {CANARY_COMMAND}");

    let captured = log.bytes();
    assert!(contains_bytes(&captured, CANARY_PASSWORD.as_bytes()));
    assert!(contains_bytes(&captured, CANARY_COMMAND.as_bytes()));
    assert!(!contains_bytes(&captured, b"CANARY-never-planted"));
}

#[tokio::test]
async fn streaming_rpcs_refused_at_the_start_yield_one_line_with_the_terminal_code() {
    let log = CapturedLog::start();
    let agent = InProcessAgent::start().await;
    let channel = agent.channel();

    let mut install = PhpServiceClient::new(channel.clone())
        .install_php_version(InstallPhpVersionRequest {
            version: "not-a-version".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    let mut install_code = None;
    while let Some(message) = install.message().await.unwrap() {
        if let Some(install_php_version_response::Result::Error(error)) = message.result {
            install_code = Some(code_name(error.code));
        }
    }
    let install_code = install_code.expect("the stream ends with a typed refusal");
    audit_lines_after_waiting_for(&log, 1).await;

    let mut backup = BackupServiceClient::new(channel)
        .create_backup(CreateBackupRequest {
            account_username: INVALID_NAME.to_owned(),
            backup_id: "audit-probe".to_owned(),
            destination: Some(BackupDestination {
                kind: BackupDestinationKind::Local as i32,
                path: "/nonexistent".to_owned(),
                ..Default::default()
            }),
        })
        .await
        .unwrap()
        .into_inner();
    let mut backup_code = None;
    while let Some(message) = backup.message().await.unwrap() {
        if let Some(create_backup_response::Result::Error(error)) = message.result {
            backup_code = Some(code_name(error.code));
        }
    }
    let backup_code = backup_code.expect("the stream ends with a typed refusal");

    let lines = audit_lines_after_waiting_for(&log, 2).await;
    assert_eq!(lines.len(), 2, "one line per stream: {lines:?}");
    assert_line(
        &lines,
        0,
        "maran.agent.v1.PhpService/InstallPhpVersion",
        &install_code,
    );
    assert_line(
        &lines,
        1,
        "maran.agent.v1.BackupService/CreateBackup",
        &backup_code,
    );
}

#[tokio::test]
async fn a_stream_dropped_by_the_client_still_yields_exactly_one_line() {
    let log = CapturedLog::start();
    let agent = InProcessAgent::start().await;
    let channel = agent.channel();

    let stream = PhpServiceClient::new(channel.clone())
        .install_php_version(InstallPhpVersionRequest {
            version: "not-a-version".to_owned(),
        })
        .await
        .unwrap()
        .into_inner();
    // The client leaves without reading a message: the server sees the stream reset.
    drop(stream);
    audit_lines_after_waiting_for(&log, 1).await;

    // A second mutating rpc is the proof that the first one did not write a second
    // line later: its own line is the second, and nothing sits between them.
    refused_create_account(channel).await;
    let lines = audit_lines_after_waiting_for(&log, 2).await;
    assert_eq!(
        lines.len(),
        2,
        "exactly one line for the dropped stream: {lines:?}"
    );
    assert_eq!(
        field(&lines[0], "method"),
        "maran.agent.v1.PhpService/InstallPhpVersion"
    );
    assert_eq!(
        field(&lines[1], "method"),
        "maran.agent.v1.AccountsService/CreateAccount"
    );
}

#[tokio::test]
async fn a_peer_the_guard_refuses_still_leaves_a_line_for_a_mutating_path() {
    let log = CapturedLog::start();
    // Admits the NEXT uid, so this process is the refused caller (the same device
    // handshake.rs uses; a test that had to be another user would not run).
    let uid = maran_agent_core::utils::current_uid::current_uid().unwrap();
    let agent = InProcessAgent::start_admitting(uid.wrapping_add(1)).await;
    let channel = agent.channel();

    let refused = AccountsServiceClient::new(channel.clone())
        .create_account(CreateAccountRequest {
            username: "root".to_owned(),
            quota_bytes: 0,
            ..Default::default()
        })
        .await
        .unwrap_err();
    assert_eq!(refused.code(), tonic::Code::PermissionDenied);

    // A read-only path from the same refused peer writes nothing.
    SystemServiceClient::new(channel)
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .unwrap_err();

    let lines = audit_lines_after_waiting_for(&log, 1).await;
    assert_eq!(lines.len(), 1, "{lines:?}");
    assert_line(
        &lines,
        0,
        "maran.agent.v1.AccountsService/CreateAccount",
        "transport:PermissionDenied",
    );
}
