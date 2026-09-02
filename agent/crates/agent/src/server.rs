//! Server assembly: socket preparation, peer-cred enforcement, service registry.

use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::Path;

use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::db::ProcessDbHost;
use maran_ops::files::ProcessFilesHost;
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
use crate::proto::db_service_server::DbServiceServer;
use crate::proto::files_service_server::FilesServiceServer;
use crate::proto::php_service_server::PhpServiceServer;
use crate::proto::sftp_service_server::SftpServiceServer;
use crate::proto::sites_service_server::SitesServiceServer;
use crate::proto::ssl_service_server::SslServiceServer;
use crate::proto::system_service_server::SystemServiceServer;
use crate::services::accounts::accounts_service::AccountsServiceImpl;
use crate::services::db::db_service::DbServiceImpl;
use crate::services::files::files_service::FilesServiceImpl;
use crate::services::php::php_service::PhpServiceImpl;
use crate::services::sftp::sftp_service::SftpServiceImpl;
use crate::services::sites::sites_service::SitesServiceImpl;
use crate::services::ssl::ssl_service::SslServiceImpl;
use crate::services::system::system_service::SystemServiceImpl;

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

    Server::builder()
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
            ),
            PeerGuard::new(policy),
        ))
        .add_service(SitesServiceServer::with_interceptor(
            SitesServiceImpl::new(ProcessSiteHost::new(), ProcessPhpHost::new(), adapter),
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
        .serve_with_incoming(UnixListenerStream::new(listener))
        .await?;

    Ok(())
}
