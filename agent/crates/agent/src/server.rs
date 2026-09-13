//! Server assembly: socket preparation, peer-cred enforcement, service registry.

use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::Path;

use maran_distro::DistroAdapter;
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::backup::ProcessBackupHost;
use maran_ops::cron::ProcessCronHost;
use maran_ops::db::ProcessDbHost;
use maran_ops::files::ProcessFilesHost;
use maran_ops::firewall::ProcessFirewallHost;
use maran_ops::ftps::ProcessFtpsHost;
use maran_ops::logins::ProcessLoginsHost;
use maran_ops::monitor::ProcessMonitorHost;
use maran_ops::php::{
    PhpHost, PoolTreeDecision, PoolTreeOutcome, ProcessPhpHost, reload_pool_trees,
};
use maran_ops::sftp::ProcessSftpHost;
use maran_ops::sites::{ProcessSiteHost, SiteMaintenanceHost, SitesOpError, reload_web_server};
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
use crate::proto::ftps_service_server::FtpsServiceServer;
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
use crate::services::ftps::ftps_service::FtpsServiceImpl;
use crate::services::monitor::monitor_service::MonitorServiceImpl;
use crate::services::php::php_service::PhpServiceImpl;
use crate::services::sftp::sftp_service::SftpServiceImpl;
use crate::services::sites::sites_service::SitesServiceImpl;
use crate::services::ssl::ssl_service::SslServiceImpl;
use crate::services::system::system_service::SystemServiceImpl;
use crate::shutdown::{StopSignals, drain_deadline};

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
    // blocking thread with NO timeout (tokio 1.53 `runtime::blocking::pool`),
    // so the exit waits for whatever `useradd`, `rename` or database load was
    // already running. The real bound on a stop is therefore the unit's
    // `TimeoutStopSec=45` and the `SIGKILL` behind it, not this budget.
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

/// What [`reconcile_web_tree`] found and did, so a test can observe the
/// decision rather than the log line it produced.
///
/// Three outcomes and not a `Result`, because the two failures are different
/// jobs for an operator and are logged at different levels: a tree the
/// validator rejects is a broken file somebody must fix, and a reload that
/// refuses on an accepted tree is usually a web server that is not running
/// yet.
#[derive(Debug, PartialEq, Eq)]
enum WebTreeReconciliation {
    /// The tree on disk validated and the running server was reloaded onto it.
    Applied,
    /// The validator rejected what is on disk. Nothing was reloaded.
    Refused,
    /// The tree validated but the reload did not take.
    NotReloaded,
}

/// Validates the web configuration tree on disk at startup and, when it
/// passes, reloads the running server onto it.
///
/// # Why this exists
///
/// `ops::safe_write` renames new content over the live target BEFORE
/// validating it, deliberately and correctly: the validating tool reads the
/// real tree by path, and a temporary file matches no include glob. The cost
/// is an on-disk midpoint. A process killed between that rename and the
/// commit leaves content no validator has accepted at the real path, with the
/// `RollbackGuard` that would have undone it gone with the process — and the
/// running server unaffected, because it has not been asked to reload. Until
/// this call existed, nothing on the host ever looked: the next reload from
/// any cause was the first thing to find out, and at a restart or a reboot
/// that means every site on the host failing to come up at once.
///
/// # Why observation rather than a record of intent
///
/// A journal written before each rename would say more — which write was
/// killed, and what to put back. It is refused here because it is a file of
/// the agent's own on every customer host, which `rules/architecture.md`
/// forbids ("the agent MUST stay stateless"), and because a record that
/// disagrees with the disk is wrong and cannot know it. Re-validating observes
/// the artefact the system actually uses, on the path it reads it from
/// (`rules/testing.md`), needs no new state, and catches every cause of a bad
/// tree rather than only this one.
///
/// # What it does NOT observe
///
/// Only the web tree — the vhosts under `AgentPaths::NGINX_INCLUDE_DIRECTORY`
/// and the TLS material `nginx -t` loads with them. The php-fpm pool trees are
/// [`reconcile_php_pool_trees`]'s, beside this call, and are no longer a blind
/// spot: they are version-keyed, so that pass enumerates the installed
/// versions first and answers one outcome per version.
///
/// What remains unobserved is the two systemd jail-mount trees and
/// `vsftpd.conf`. They cannot be observed this way at all: they pass a MUTATING
/// command as their validator (`systemctl daemon-reload`, and `systemctl
/// restart` for FTPS), so re-running it at a start would restart the daemon and
/// drop every live session rather than ask it a question. Closing that costs a
/// PURE validator per tree, which is what does not exist here: `vsftpd` has no
/// `-t`, and a systemd unit file has no check-without-load reachable through
/// this workspace's `DistroAdapter`. The ruling about those three is stated in
/// full on `maran_ops::safe_write::model::Validator`, which is where the
/// mutating validators are constructed and counted; what this file does about
/// them is decline to re-run an action and say so. The blind spot is
/// named in this function's own log output rather than only here, and
/// `the_only_startup_reconciliations_are_the_restores_the_web_tree_and_the_php_pools`
/// goes red the day one of the three gains a pass.
///
/// # Why it never refuses the start
///
/// This process is the operator's only remote repair tool. Refusing to start
/// on a bad tree removes it at exactly the moment it is needed, and takes
/// accounts, databases, backups and monitoring down for a defect in one file —
/// the same trade the backup-dependency check above is decided by. There is
/// also nothing to roll back to: the previous content lived only in the dead
/// process's memory, and the only undo available here would be to delete the
/// file, which destroys a site rather than restoring one.
fn reconcile_web_tree(
    host: &dyn SiteMaintenanceHost,
    distro: &dyn DistroAdapter,
) -> WebTreeReconciliation {
    match reload_web_server(host, distro) {
        Ok(()) => {
            tracing::info!(
                "the web configuration tree on disk validated and the running server was \
                 reloaded onto it. UNOBSERVED HERE: the SFTP and FTPS jail mount units \
                 and vsftpd.conf — those three trees have no validator that can be run \
                 without also acting on the daemon. The php-fpm pool trees ARE observed, \
                 by reconcile_php_pool_trees, one outcome per installed version"
            );
            WebTreeReconciliation::Applied
        }
        Err(SitesOpError::NginxValidation { stderr }) => {
            tracing::error!(
                %stderr,
                "the web configuration tree on disk is REJECTED by its own validator at \
                 startup, so it was not reloaded. Something wrote this tree without \
                 finishing — a config write killed between its rename and its \
                 validation is the case this check exists for. The running server is \
                 unaffected and will refuse a reload, but it will FAIL TO START at the \
                 next restart or reboot, and every config write on this host will fail \
                 validation until the named file is fixed by hand. Nothing here can undo \
                 it: the previous content is gone with the process that held it"
            );
            WebTreeReconciliation::Refused
        }
        Err(error) => {
            tracing::warn!(
                %error,
                "the web configuration tree validated but the reload did not take; the \
                 running server may still be serving an older configuration than the one \
                 on disk"
            );
            WebTreeReconciliation::NotReloaded
        }
    }
}

/// Validates every installed PHP version's php-fpm pool tree on disk at
/// startup and, for each that passes, reloads that version's service onto it.
///
/// # Why this exists, and why it is separate from the web pass
///
/// The same on-disk midpoint, in the other tree `ops::safe_write` renames
/// into. `php-fpm -t` reads the pool directory by path, so the rename precedes
/// the validation there for the same reason it does under nginx, and a process
/// killed in that window leaves a pool file no validator accepted at the real
/// path with the `RollbackGuard` gone. The consequence is sharper than nginx's
/// in one respect: php-fpm refuses to START on a pool file it cannot parse, so
/// at the next restart every site on the host using that version stops serving
/// at once, and the panel's own screens say the pool was written.
///
/// It is a second function rather than an arm of [`reconcile_web_tree`] because
/// the answer is per VERSION, not per host: `php-fpm8.3 -t` reads only `8.3`'s
/// pool directory, so one broken file says nothing about the other versions and
/// must not be reported as though it did. Folding them would also lose the
/// version an operator has to go and fix, which is the only actionable part of
/// the message.
///
/// # What it observes, and what it still cannot
///
/// It observes the artefact the daemon reads, on the path it reads it from,
/// through the same validator and the same service name every individual pool
/// write uses — so it needs no state of its own, which is what
/// `rules/architecture.md` requires of this process ("the agent MUST stay
/// stateless"). What it cannot see is WHICH write was killed, or anything that
/// happened while this process was not running: it reports on the tree as it is
/// now, never on the history that produced it. A version with no pool directory
/// is not installed and yields no outcome, which is the honest answer and not a
/// silence — there is no tree there to observe.
///
/// # Why it never refuses the start
///
/// The same trade [`reconcile_web_tree`] is decided by, and it is not re-argued
/// here: this process is the operator's only remote repair tool, and there is
/// nothing to roll back to, because the previous pool file died with the
/// process that held it in memory.
fn reconcile_php_pool_trees(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
) -> Vec<PoolTreeOutcome> {
    let outcomes = reload_pool_trees(host, distro);

    if outcomes.is_empty() {
        tracing::info!(
            "no PHP version is installed, so there is no php-fpm pool tree to validate at \
             this start"
        );
        return outcomes;
    }

    for outcome in &outcomes {
        match outcome.decision {
            PoolTreeDecision::Applied => tracing::info!(
                version = %outcome.version,
                "this version's php-fpm pool tree on disk validated and its service was \
                 reloaded onto it"
            ),
            PoolTreeDecision::Refused => tracing::error!(
                version = %outcome.version,
                stderr = %outcome.stderr,
                "this version's php-fpm pool tree on disk is REJECTED by its own \
                 validator at startup, so it was not reloaded. Something wrote this tree \
                 without finishing — a pool write killed between its rename and its \
                 validation is the case this check exists for. The running php-fpm is \
                 unaffected and will refuse a reload, but it will FAIL TO START at the \
                 next restart or reboot, and every site on this host using this version \
                 stops serving when it does. Nothing here can undo it: the previous pool \
                 file is gone with the process that held it"
            ),
            PoolTreeDecision::NotReloaded => tracing::warn!(
                version = %outcome.version,
                stderr = %outcome.stderr,
                "this version's php-fpm pool tree validated but the reload did not take; \
                 its workers may still be serving an older pool than the one on disk"
            ),
        }
    }

    outcomes
}

#[cfg(test)]
#[path = "tests/server_tests.rs"]
mod tests;
