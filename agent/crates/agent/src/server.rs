//! Server assembly: socket preparation, peer-cred enforcement, service registry.

use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::Path;

use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::backup::ProcessBackupHost;
use maran_ops::cron::ProcessCronHost;
use maran_ops::db::ProcessDbHost;
use maran_ops::files::ProcessFilesHost;
use maran_ops::firewall::ProcessFirewallHost;
use maran_ops::monitor::ProcessMonitorHost;
use maran_ops::php::ProcessPhpHost;
use maran_ops::sftp::ProcessSftpHost;
use maran_ops::sites::ProcessSiteHost;
use maran_ops::ssl::ProcessSslHost;
use tokio::net::UnixListener;
use tokio_stream::wrappers::UnixListenerStream;
use tonic::transport::Server;

use crate::error::StartupError;
use crate::peercred::{PeerGuard, PeerPolicy};
use crate::proto::accounts_service_server::AccountsServiceServer;
use crate::proto::backup_service_server::BackupServiceServer;
use crate::proto::cron_service_server::CronServiceServer;
use crate::proto::db_service_server::DbServiceServer;
use crate::proto::files_service_server::FilesServiceServer;
use crate::proto::firewall_service_server::FirewallServiceServer;
use crate::proto::monitor_service_server::MonitorServiceServer;
use crate::proto::php_service_server::PhpServiceServer;
use crate::proto::sftp_service_server::SftpServiceServer;
use crate::proto::sites_service_server::SitesServiceServer;
use crate::proto::ssl_service_server::SslServiceServer;
use crate::proto::system_service_server::SystemServiceServer;
use crate::services::accounts::accounts_service::AccountsServiceImpl;
use crate::services::backup::backup_service::BackupServiceImpl;
use crate::services::cron::cron_service::CronServiceImpl;
use crate::services::db::db_service::DbServiceImpl;
use crate::services::files::files_service::FilesServiceImpl;
use crate::services::firewall::firewall_service::FirewallServiceImpl;
use crate::services::monitor::monitor_service::MonitorServiceImpl;
use crate::services::php::php_service::PhpServiceImpl;
use crate::services::sftp::sftp_service::SftpServiceImpl;
use crate::services::sites::sites_service::SitesServiceImpl;
use crate::services::ssl::ssl_service::SslServiceImpl;
use crate::services::system::system_service::SystemServiceImpl;
use crate::shutdown::{drain_deadline, shutdown_signal};

/// Permissions the socket is created with: owner and group only.
///
/// The agent runs as root, so the socket's group is what lets the panel user
/// reach it; world access would hand the panel's full command set to every local
/// account on the machine.
const SOCKET_MODE: u32 = 0o660;

/// Permissions the socket's directory is created with: owner and group traversal
/// only. In production systemd owns `/run/maran` and applies the same mode; this
/// covers a developer run, where the directory is created here.
const DIRECTORY_MODE: u32 = 0o750;

/// Binds `socket_path` and serves the contract until the process is stopped.
///
/// # Errors
///
/// Returns [`StartupError`] when the socket cannot be prepared, the host is
/// unsupported, or the server terminates abnormally.
pub async fn serve(socket_path: &Path, policy: PeerPolicy) -> Result<(), StartupError> {
    // Detection first: refusing an unsupported host before the socket exists
    // means the panel sees "agent absent" rather than an agent that accepts
    // connections and then fails every operation.
    let distro = maran_distro::detect()?;

    // The directory is created before the socket and no wider than the socket
    // itself, because binding is not atomic with respect to permissions: the
    // kernel creates the socket using the process umask — typically world
    // readable — and only the next statement narrows it. A directory nobody else
    // can traverse closes that window without a umask call, which would mean an
    // `unsafe` libc wrapper outside the one module allowed to hold them
    // (rules/rust.md).
    if let Some(directory) = socket_path.parent() {
        std::fs::DirBuilder::new()
            .recursive(true)
            .mode(DIRECTORY_MODE)
            .create(directory)?;
    }

    // A socket file left by a killed process would make bind fail with
    // "address in use", so the stale entry is removed rather than reported.
    if socket_path.exists() {
        std::fs::remove_file(socket_path)?;
    }

    let listener = UnixListener::bind(socket_path)?;
    std::fs::set_permissions(socket_path, std::fs::Permissions::from_mode(SOCKET_MODE))?;

    tracing::info!(
        socket = %socket_path.display(),
        distro = %distro.id,
        version = %distro.version_id,
        "agent listening"
    );

    // Read before the DistroInfo is handed to the system service, which takes ownership of it.
    let adapter = maran_distro::adapter_for(distro.family);

    // Advisory, not a startup refusal. The paragraph here used to say that
    // backups "are not yet wired into this service registry"; they are, from
    // the `BackupServiceServer` below, so a missing `tar`, `gzip` or dump
    // client now really does break an rpc this process serves. It is still a
    // warning rather than a refusal, and the trade is the one that was always
    // meant: a hard exit would take accounts, sites, cron, firewall and
    // monitoring down with it for a dependency none of them touches, and every
    // backup rpc reports the absence itself, by name, as it fails. Logged at
    // startup because that is the earliest a real host can be asked — what it
    // cannot see is a package removed AFTER this line runs, while the daemon
    // keeps serving; `verify_backup_binaries`'s own doc carries that limit.
    if let Err(error) =
        maran_ops::backup::verify_backup_binaries(adapter, &maran_ops::backup::RealExecutableLookup)
    {
        tracing::warn!(
            %error,
            "a backup dependency is missing; scheduled backups will fail until it is restored"
        );
    }

    let (signalled, deadline) = tokio::sync::oneshot::channel();

    let serving = Server::builder()
        .add_service(SystemServiceServer::with_interceptor(
            SystemServiceImpl::new(distro),
            PeerGuard::new(policy),
        ))
        // Every service carries the same guard: the interceptor is per service, so a
        // service registered without one would be reachable by any local process
        // that can open the socket, whatever the others require.
        .add_service(AccountsServiceServer::with_interceptor(
            AccountsServiceImpl::new(
                AccountOperations::new(ProcessSystemHost::new(adapter), adapter),
                ProcessPhpHost::new(),
                ProcessDbHost::new(adapter),
                ProcessSftpHost::new(),
                // Read-only here: the suspension state reads vhosts, it never
                // writes one. What a suspended account serves is written by
                // the site service, driven per site by the panel.
                ProcessSiteHost::new(),
                // Read-only here too: the suspension state reads the crontab,
                // it never installs one. What a suspended account's crontab
                // says is written by the cron service.
                ProcessCronHost::new(adapter),
            ),
            PeerGuard::new(policy),
        ))
        .add_service(SitesServiceServer::with_interceptor(
            SitesServiceImpl::new(ProcessSslHost::new(), ProcessPhpHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        .add_service(SslServiceServer::with_interceptor(
            SslServiceImpl::new(ProcessSslHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        .add_service(PhpServiceServer::with_interceptor(
            PhpServiceImpl::new(ProcessPhpHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // No distro adapter: this service creates and removes files inside a
        // customer's home, and where an account's home is is the same fact on
        // every family (`AgentPaths`). A service that took an adapter it never
        // asked a question of would suggest there is a platform difference
        // here, and there is not.
        .add_service(FilesServiceServer::with_interceptor(
            FilesServiceImpl::new(ProcessFilesHost::new()),
            PeerGuard::new(policy),
        ))
        // No distro adapter either, and for the same kind of reason: the one
        // platform fact the database area needs is the client's path, and
        // `ProcessDbHost` takes it from the adapter at construction. Nothing an
        // rpc does afterwards depends on the family.
        .add_service(DbServiceServer::with_interceptor(
            DbServiceImpl::new(ProcessDbHost::new(adapter)),
            PeerGuard::new(policy),
        ))
        .add_service(SftpServiceServer::with_interceptor(
            SftpServiceImpl::new(ProcessSftpHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // The adapter is passed on: a crontab line names the interpreter by
        // absolute path, which differs per family, and `crontab(1)` itself is
        // asked for by path too.
        .add_service(CronServiceServer::with_interceptor(
            CronServiceImpl::new(ProcessCronHost::new(adapter), adapter),
            PeerGuard::new(policy),
        ))
        // The adapter again, for `nft`'s own path. Where the RULES live does
        // not differ between families — the agent renders and replaces its own
        // files (`AgentPaths`) — so nothing else here is a platform question.
        .add_service(FirewallServiceServer::with_interceptor(
            FirewallServiceImpl::new(ProcessFirewallHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // And once more, for the two facts monitoring needs: where the
        // password database lives, and what this family calls each of the
        // units the panel watches.
        .add_service(MonitorServiceServer::with_interceptor(
            MonitorServiceImpl::new(ProcessMonitorHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // Two hosts and the adapter: an archive is `tar` and a compressor,
        // both platform binaries, and a backup's databases are dumped and
        // reloaded through the same client the database service uses — which
        // is what the `DatabaseCatalog` seam is wired with here rather than in
        // `ops`, since `ops::backup` does not import another `ops` area. The
        // adapter answers one more question of its own: what this family calls
        // the web server's group, which a restore re-applies to the home root.
        .add_service(BackupServiceServer::with_interceptor(
            BackupServiceImpl::new(
                ProcessBackupHost::new(adapter),
                ProcessDbHost::new(adapter),
                adapter,
            ),
            PeerGuard::new(policy),
        ))
        .serve_with_incoming_shutdown(UnixListenerStream::new(listener), async move {
            shutdown_signal().await;
            // Failure means the deadline half was dropped, which only happens
            // when the whole select below is going away: nothing to report.
            let _ = signalled.send(());
        });

    // The drain is BOUNDED, and the bound is not a safety valve — it is the
    // reason this is a graceful stop at all. `sites.TailSiteLog` streams for as
    // long as its client reads, and the panel's log viewer holds one open, so
    // waiting for every in-flight request to end means waiting for a browser
    // tab. An unbounded drain would turn every `systemctl stop` into a wait for
    // `TimeoutStopSec` followed by the same `SIGKILL` we started with — slower,
    // and no safer. See `shutdown::DRAIN_BUDGET` for what the budget is sized
    // against and, explicitly, what it cannot save.
    tokio::select! {
        result = serving => result?,
        () = drain_deadline(deadline) => tracing::warn!(
            budget_seconds = crate::shutdown::DRAIN_BUDGET.as_secs(),
            "work still in flight when the drain budget expired; stopping anyway"
        ),
    }

    // The socket is removed on the way out so that a stopped agent leaves no
    // entry the panel can connect to and hang on. It is belt to `serve`'s
    // braces: the bind above already unlinks a stale socket, because a crash
    // reaches neither this line nor any other.
    let _ = std::fs::remove_file(socket_path);

    Ok(())
}
