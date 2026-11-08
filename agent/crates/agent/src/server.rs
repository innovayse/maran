//! Server assembly: socket preparation, peer-cred enforcement, service registry.

use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::backup::{ProcessBackupHost, ProcessRestrictedDumpHost};
use maran_ops::cron::ProcessCronHost;
use maran_ops::db::ProcessDbHost;
use maran_ops::dns::ProcessDnsHost;
use maran_ops::files::{ProcessArchiveHost, ProcessBrowseHost, ProcessFilesHost};
use maran_ops::firewall::ProcessFirewallHost;
use maran_ops::ftps::ProcessFtpsHost;
use maran_ops::logins::ProcessLoginsHost;
use maran_ops::mail::ProcessMailHost;
use maran_ops::monitor::ProcessMonitorHost;
use maran_ops::php::ProcessPhpHost;
use maran_ops::sftp::ProcessSftpHost;
use maran_ops::sites::ProcessSiteHost;
use maran_ops::ssl::{ProcessPanelChallengeHost, ProcessSslHost};
use tonic::transport::Server;

use crate::audit::audit_layer::AuditLayer;
use crate::error::StartupError;
use crate::fleet::agent_incoming::agent_incoming;
use crate::fleet::fleet_admission_layer::FleetAdmissionLayer;
use crate::fleet::fleet_listener::FleetListener;
use crate::peercred::{PeerGuard, PeerPolicy};
use crate::proto::accounts_service_server::AccountsServiceServer;
use crate::proto::backup_service_server::BackupServiceServer;
use crate::proto::cron_service_server::CronServiceServer;
use crate::proto::db_service_server::DbServiceServer;
use crate::proto::dns_service_server::DnsServiceServer;
use crate::proto::files_service_server::FilesServiceServer;
use crate::proto::firewall_service_server::FirewallServiceServer;
use crate::proto::ftps_service_server::FtpsServiceServer;
use crate::proto::mail_service_server::MailServiceServer;
use crate::proto::monitor_service_server::MonitorServiceServer;
use crate::proto::php_service_server::PhpServiceServer;
use crate::proto::sftp_service_server::SftpServiceServer;
use crate::proto::sites_service_server::SitesServiceServer;
use crate::proto::ssl_service_server::SslServiceServer;
use crate::proto::system_service_server::SystemServiceServer;
use crate::proto::transfer_service_server::TransferServiceServer;
use crate::services::accounts::accounts_service::AccountsServiceImpl;
use crate::services::backup::backup_service::BackupServiceImpl;
use crate::services::cron::cron_service::CronServiceImpl;
use crate::services::db::db_service::DbServiceImpl;
use crate::services::dns::dns_service::DnsServiceImpl;
use crate::services::files::files_service::FilesServiceImpl;
use crate::services::firewall::firewall_service::FirewallServiceImpl;
use crate::services::ftps::ftps_service::FtpsServiceImpl;
use crate::services::mail::mail_service::MailServiceImpl;
use crate::services::monitor::monitor_service::MonitorServiceImpl;
use crate::services::php::php_service::PhpServiceImpl;
use crate::services::sftp::sftp_service::SftpServiceImpl;
use crate::services::sites::sites_service::SitesServiceImpl;
use crate::services::ssl::ssl_service::SslServiceImpl;
use crate::services::system::system_service::SystemServiceImpl;
use crate::services::transfer::transfer_service::TransferServiceImpl;
use crate::shutdown::{StopSignals, drain_deadline};
use crate::startup::bind_socket::bind_socket;
use crate::startup::reconcile_dns_zones::reconcile_dns_zones;
use crate::startup::reconcile_import_users::reconcile_import_users;
use crate::startup::reconcile_opcache_drop_ins::reconcile_opcache_drop_ins;
use crate::startup::reconcile_php_pool_trees::reconcile_php_pool_trees;
use crate::startup::reconcile_sftp_key_files::reconcile_sftp_key_files;
use crate::startup::reconcile_site_auth_files::reconcile_site_auth_files;
use crate::startup::reconcile_web_tree::reconcile_web_tree;

/// Binds `socket_path` and serves the contract until the process is stopped.
///
/// `floor` is the free-space floor the writing operations refuse below
/// (issue #148): account creation, backup creation, restore, upload, and the
/// panel's own ACME challenge file (issue #132). It is handed to each of their
/// services here and to the monitor service, which reports it so the panel can
/// warn before the refusals begin.
///
/// `fleet` is the TCP listener of fleet mode (issue #180), and `None` is the
/// shipped default: no second socket, no TLS code run, nothing listening but the
/// unix socket. When it is given, the same router and the same services answer
/// both listeners; only who is admitted differs (`fleet::fleet_admission_service`
/// for a network peer, `PeerGuard`'s uid check for a local one).
///
/// # Errors
///
/// Returns [`StartupError`] when the socket cannot be prepared, the fleet
/// listener cannot be bound or configured, the host is unsupported, or the
/// server terminates abnormally.
pub async fn serve(
    socket_path: &Path,
    policy: PeerPolicy,
    floor: FreeSpaceFloor,
    fleet: Option<FleetListener>,
) -> Result<(), StartupError> {
    // Detection first: refusing an unsupported host before the socket exists
    // means the panel sees "agent absent" rather than an agent that accepts
    // connections and then fails every operation.
    let distro = maran_distro::detect()?;

    // BEFORE the socket exists, and the ordering is the whole safety argument
    // rather than a preference. A restore that was killed between its two
    // renames leaves an account's home parked under
    // `AgentPaths::RESTORE_STAGING_ROOT` and `/home/<account>` absent; nothing
    // used to put it back, and the panel's retry failed ENOENT for the life of
    // the host. This finishes what the killed process started — and, in the same
    // pass, empties the bulk scratch of the half a retry can reproduce while
    // KEEPING the pre-restore database dumps, which the unit file used to delete
    // with `rm -rf` on the very restart an operator performs to recover.
    //
    // Here and not after the bind: `recover_restores` renames directories into
    // `/home`, and a RestoreBackup rpc accepted while it ran would be a second
    // writer of the same paths. Its per-account lock covers that structurally,
    // but the reason there is nothing to race is that no connection has been
    // accepted yet. Moving this line below `UnixListener::bind` breaks that,
    // silently, so it says so here as well as on the function.
    //
    // It answers a tally rather than nothing, and the tally is logged: an
    // operator reconciling the panel's row against the host reads it here, and
    // it is the only place that will say a customer's home was put back.
    let recovery = maran_ops::backup::recover_restores();
    tracing::info!(
        swaps_completed = recovery.swaps_completed,
        swaps_rolled_back = recovery.swaps_rolled_back,
        swaps_already_done = recovery.swaps_already_done,
        swaps_abandoned = recovery.swaps_abandoned,
        refused = recovery.refused,
        unmarked_left = recovery.unmarked_left,
        rollback_sets_kept = recovery.rollback_sets_kept,
        scratch_entries_removed = recovery.scratch_entries_removed,
        "interrupted restores reconciled and the bulk scratch reaped"
    );

    // BEFORE the socket, and for the same kind of reason the reconciliation
    // above runs before it: from the instant the socket exists, the panel can
    // dial this process and an operator can stop it, so from that instant the
    // stop must be the handled one. Installing the handlers inside the shutdown
    // future — which is where they used to be — meant they were installed when
    // that future was FIRST POLLED, which is after the bind, after `agent
    // listening` is logged and after the backup dependencies are checked. A
    // `SIGTERM` arriving in that window took its default action: the daemon died
    // by signal instead of exiting, and the socket file below was never removed,
    // so the panel was left dialling a daemon that is gone. Measured, not
    // reasoned: under 24 concurrent CPU-bound processes on a 12-core host the
    // window was hit by 11 of 60 runs of the shutdown suite and by 5 of 40 runs
    // of a probe on the shipped binary, every one of which left the socket
    // behind. Moving this line below the bind restores that window silently, so
    // it says so here as well as on `StopSignals::install`.
    let stop_signals = StopSignals::install();

    // Read here rather than after the bind, because the web-tree reconciliation
    // below needs it and that has to run before any connection is accepted. The
    // `DistroInfo` is handed to the system service further down, which takes
    // ownership of it, so the family is read while it is still here.
    let adapter = maran_distro::adapter_for(distro.family);
    // The release, for the same reason: the system service below takes the
    // `DistroInfo` whole, and mail's Dovecot generation is a fact of the release.
    let release = distro.version_id.clone();

    // BEFORE the socket, and for the same reason as the restore reconciliation
    // above: `reload_web_server` does NOT take `safe_write::config_tree_lock`,
    // so a `CreateSite` accepted while it ran would be writing the very tree it
    // is validating. No connection has been accepted yet, so there is nothing
    // to race.
    reconcile_web_tree(&ProcessSiteHost::new(), adapter);

    // And the same window over the OTHER tree `safe_write` renames into: the
    // php-fpm pools. Here for the same two reasons — the pass takes no
    // `config_tree_lock`, so a `WritePool` accepted while it ran would be
    // writing the very tree it is validating, and no connection has been
    // accepted yet.
    reconcile_php_pool_trees(&ProcessPhpHost::new(), adapter);

    // Each installed PHP version's OPcache drop-in, the isolation settings of the
    // cache every tenant of a version shares. After the pool-tree pass so a tree
    // it found broken is reported before a reload is asked of the same master, and
    // before the socket for the same reason as the passes around it.
    reconcile_opcache_drop_ins(&ProcessPhpHost::new(), adapter);

    // The SFTP logins' key directory against the password database (issue
    // #338). BEFORE the socket for the same reason as the passes above: no
    // `CreateSftpUser` or `SetSftpAuthorizedKeys` can be writing a key file
    // while the sweep decides which ones are orphans, because no connection has
    // been accepted yet.
    reconcile_sftp_key_files(&ProcessSftpHost::new(), adapter);

    // The sites' credential files against the vhost directory (issue #151).
    // BEFORE the socket, for the same reason as the passes above: no `CreateSite`
    // can be writing a credential file and a vhost while the sweep decides which
    // files are orphans, because no connection has been accepted yet.
    reconcile_site_auth_files(&ProcessSiteHost::new());

    // The zone files against the DNS daemon's own zone list (issue #178). BEFORE
    // the socket, so no `PutZone` or `DeleteZone` can be changing a zone while the
    // pass decides which files the daemon has lost, because no connection has
    // been accepted yet. A host that has not enabled DNS has no daemon to ask,
    // which is not a fault.
    reconcile_dns_zones(&ProcessDnsHost::new(), adapter);

    // The temporary database users an interrupted import left behind (issue
    // #127). BEFORE the socket, so no `ImportBackup` can be between creating its
    // user and dropping it while the pass drops every user of that shape,
    // because no connection has been accepted yet.
    reconcile_import_users(&ProcessRestrictedDumpHost::new(
        adapter,
        PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
    ));

    // The fleet listener's address is bound BEFORE the unix socket: the socket file is what
    // tells the panel and the tests that the agent is ready, so a node whose fleet address is
    // taken or whose identity is unusable must fail here, leaving no socket behind. Nothing is
    // accepted on it until the server starts below.
    let fleet = match fleet {
        Some(listener) => Some(listener.bind().await?),
        None => None,
    };
    let fleet_admission = fleet
        .as_ref()
        .map_or_else(FleetAdmissionLayer::refusing_every_fleet_peer, |bound| {
            bound.admission_layer()
        });

    // The socket directory, a stale socket and the bind itself are `bind_socket`'s.
    let listener = bind_socket(socket_path)?;

    tracing::info!(
        socket = %socket_path.display(),
        distro = %distro.id,
        version = %distro.version_id,
        "agent listening"
    );

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
        // The audit layer wraps the whole router, so it sits OUTSIDE the per-service
        // `PeerGuard` below: a peer the guard refuses on a mutating path still leaves
        // a line (`outcome=transport:PermissionDenied`). It writes one line per
        // mutating rpc and nothing for any other path, and changes no request and no
        // response (issue #106, docs/superpowers/notes/2026-11-06-agent-audit-lines-threat-note.md).
        .layer(AuditLayer)
        // Inside the audit layer and outside every `PeerGuard`: a request from the fleet
        // listener is checked against the pinned panel certificate before any service sees
        // it, and a refused one still leaves its audit line. Requests from the unix socket
        // pass through it untouched.
        .layer(fleet_admission)
        .add_service(SystemServiceServer::with_interceptor(
            SystemServiceImpl::new(distro),
            PeerGuard::new(policy),
        ))
        // Every service carries the same guard: the interceptor is per service, so a
        // service registered without one would be reachable by any local process
        // that can open the socket, whatever the others require.
        .add_service(AccountsServiceServer::with_interceptor(
            AccountsServiceImpl::new(
                AccountOperations::new(ProcessSystemHost::new(adapter), adapter, floor),
                ProcessPhpHost::new(),
                ProcessDbHost::new(adapter),
                ProcessSftpHost::new(),
                // The FTPS area's machine, for the second half of the login
                // teardown: an account's FTPS logins live in their own jail
                // under their own root, and the SFTP host is blind to them.
                ProcessFtpsHost::new(),
                // Read-only here: the suspension state reads vhosts, it never
                // writes one. What a suspended account serves is written by
                // the site service, driven per site by the panel.
                ProcessSiteHost::new(),
                // Read-only here too: the suspension state reads the crontab,
                // it never installs one. What a suspended account's crontab
                // says is written by the cron service.
                ProcessCronHost::new(adapter),
                // Read-only here as well: the suspension state reads the
                // password database for every file-transfer login the account
                // holds, of either protocol. Turning them is the sftp
                // service's `SetAccountLoginsLocked`.
                ProcessLoginsHost::new(),
            ),
            PeerGuard::new(policy),
        ))
        .add_service(SitesServiceServer::with_interceptor(
            SitesServiceImpl::new(ProcessSslHost::new(), ProcessPhpHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        .add_service(SslServiceServer::with_interceptor(
            SslServiceImpl::new(
                ProcessSslHost::new(),
                ProcessPanelChallengeHost::new(),
                floor,
                adapter,
            ),
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
        //
        // The read side is a second host and the archive side a third, each
        // built ONCE here: their concurrency bounds (per account and for the
        // whole host) are state inside the host, so a host constructed per
        // request would bound nothing.
        .add_service(FilesServiceServer::with_interceptor(
            FilesServiceImpl::new(
                ProcessFilesHost::new(),
                ProcessBrowseHost::new(),
                ProcessArchiveHost::new(),
                floor,
            ),
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
            SftpServiceImpl::new(ProcessSftpHost::new(), ProcessLoginsHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // The adapter carries every platform fact this service needs and it
        // needs a lot of them: the four shadow-suite binaries, `getent`, the
        // nologin shell, the FTPS group, the service manager, the unit
        // directory and the password database. One host and no second seam —
        // unlike the SFTP service, nothing here enumerates an account's logins
        // ACROSS protocols; the jail-scoped enumeration this area does own is
        // what keeps a password change inside one account and one daemon.
        .add_service(FtpsServiceServer::with_interceptor(
            FtpsServiceImpl::new(ProcessFtpsHost::new(), adapter),
            PeerGuard::new(policy),
        ))
        // The authoritative DNS daemon. The adapter carries the checker, the
        // control client and the service manager; the daemon arrives switched
        // off and `EnableDns` is the only thing that starts it.
        .add_service(DnsServiceServer::with_interceptor(
            DnsServiceImpl::new(ProcessDnsHost::new(), adapter, floor),
            PeerGuard::new(policy),
        ))
        // The host's mail stack. The adapter carries the daemons' programs, units
        // and the password scheme; the release picks the Dovecot generation. The
        // daemons arrive masked and `EnableMail` is the only thing that starts them.
        .add_service(MailServiceServer::with_interceptor(
            MailServiceImpl::new(ProcessMailHost::new(), adapter, release),
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
            MonitorServiceImpl::new(ProcessMonitorHost::new(), adapter, floor),
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
                floor,
            ),
            PeerGuard::new(policy),
        ))
        // Moves a backup artifact in and out (issue #127). It needs only the backup
        // host and the floor: the agent opens no connection for it, and no database
        // is touched (restoring a received artifact is the BackupService's own call).
        .add_service(TransferServiceServer::with_interceptor(
            TransferServiceImpl::new(ProcessBackupHost::new(adapter), floor),
            PeerGuard::new(policy),
        ))
        .serve_with_incoming_shutdown(agent_incoming(listener, fleet), async move {
            stop_signals.stopped().await;
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
    //
    // What the expiry arm below does and does NOT do, because the two are
    // routinely confused and this comment used to be on the wrong side of it:
    // it abandons the REQUEST, never the work. Every unit of host work runs
    // inside `spawn_blocking` (`services::wire::run_blocking` for the unary
    // rpcs, four direct spawns for the streaming ones), and dropping `serving`
    // drops a `JoinHandle` at most — a dropped handle DETACHES a blocking task,
    // it does not cancel it, and there is no tokio API that would. tonic makes
    // the gap wider still: each connection is its own `tokio::spawn`ed task
    // (`tonic::transport::server`), so dropping this future does not even reach
    // the request future; the connection task keeps running until the runtime
    // itself is torn down.
    //
    // The consequence is the one an operator needs and no line here used to
    // state: this process does not exit when the budget expires. `main` returns
    // and the runtime is dropped, and dropping a tokio runtime joins every
    // blocking thread with NO timeout of its own (tokio 1.53
    // `runtime::blocking::pool`), so the exit waits for whatever `useradd`,
    // `rename` or database load was already running. Every program the agent
    // starts now has a deadline (`CallClass`), so that wait is finite — but the
    // longest class is hours (only `Bulk`; a package install is about 35 minutes), so it is not
    // short. The real bound on a stop is therefore the unit's
    // `TimeoutStopSec=45` and the `SIGKILL` behind it, not this budget and not
    // a child's deadline.
    //
    // No tonic `timeout` or `concurrency_limit_per_connection` is set, on
    // purpose. A server timeout drops the handler future and frees nothing (the
    // blocking work and any lock it holds keep running), and it would report a
    // failure for a write that then succeeds. A concurrency limit is per
    // connection, the panel uses one HTTP/2 connection, so it would be a
    // panel-wide limit that long-lived streams such as `sites.TailSiteLog`
    // would eat slot by slot. A child that runs too long is bounded where the
    // work is, by its call class, and surfaces as the in-band `Bound` error.
    tokio::select! {
        result = serving => result?,
        () = drain_deadline(deadline) => tracing::warn!(
            budget_seconds = crate::shutdown::DRAIN_BUDGET.as_secs(),
            "the drain budget expired: this daemon has stopped answering and is \
             releasing its socket, but host work already started continues on the \
             blocking pool and the exit waits for it — systemd's TimeoutStopSec, \
             not this budget, is what bounds the stop. Nothing records which \
             operations these were; only interrupted restores are reconciled at \
             the next start"
        ),
    }

    // The socket is removed on the way out so that a stopped agent leaves no
    // entry the panel can connect to and hang on. It is belt to `serve`'s
    // braces: the bind above already unlinks a stale socket, because a crash
    // reaches neither this line nor any other.
    let _ = std::fs::remove_file(socket_path);

    Ok(())
}

#[cfg(test)]
#[path = "tests/server_tests.rs"]
mod tests;
