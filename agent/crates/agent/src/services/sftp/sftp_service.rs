//! `SftpService`: chrooted OpenSSH file-transfer logins for a hosting account.

use std::sync::Arc;

use maran_ops::sftp::{self, SftpError, SftpHost};
use tonic::{Request, Response, Status};

use crate::proto::sftp_service_server::SftpService;
use crate::proto::{
    AgentError, CreateSftpUserOk, CreateSftpUserRequest, CreateSftpUserResponse, DeleteSftpUserOk,
    DeleteSftpUserRequest, DeleteSftpUserResponse, ErrorCode, SetSftpPasswordOk,
    SetSftpPasswordRequest, SetSftpPasswordResponse, create_sftp_user_response,
    delete_sftp_user_response, set_sftp_password_response,
};
use crate::services::sftp::sftp_status::to_agent_error;
use crate::services::sftp::validated_creation::validated_creation;
use crate::services::sftp::validated_password_change::validated_password_change;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;

/// Serves the SFTP login operations over the wire.
///
/// Every rpc follows the same three steps: rebuild the request into validated
/// types, run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — a login that already exists is information,
/// not a transport error (rules/proto.md).
///
/// **No login name arrives here fully qualified and none is forwarded.**
/// `SftpUserName` has no constructor that takes a whole name, so each rpc
/// rebuilds the name from the account the panel authorised. Since two of the
/// three rpcs re-credential or revoke a login, a forwarded name would be one
/// customer taking over another customer's file access; the boundary is a type
/// rather than a check a handler could forget (see `validated_sftp_user`).
pub struct SftpServiceImpl<H> {
    /// The machine the SFTP operations run against.
    host: Arc<H>,
    /// Where platform facts come from — the `useradd`, `userdel` and `chpasswd`
    /// paths, the nologin shell, the chroot group and the unit directory. A
    /// service never branches on a distribution itself (rules/rust.md "Distro
    /// adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: SftpHost + 'static> SftpServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }

    /// Runs one operation on the blocking pool and maps its failure onto the
    /// wire error — the shape every rpc here shares, written once so that
    /// adding an rpc cannot forget to leave the runtime or map an error
    /// differently from its neighbours.
    ///
    /// Every operation here spawns a shadow-suite tool and waits for it, and
    /// creation also writes a unit file and asks the service manager to start
    /// it; rules/rust.md requires all of that off the runtime's workers, since
    /// a process wait on a worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns the [`to_agent_error`] mapping of whatever the operation failed
    /// on, or a system failure when the blocking task did not finish — a panic
    /// inside the agent has no domain answer to give, and rules/proto.md
    /// reserves gRPC statuses for transport problems, which it is not.
    async fn run<T, F>(operation: F) -> Result<T, AgentError>
    where
        F: FnOnce() -> Result<T, SftpError> + Send + 'static,
        T: Send + 'static,
    {
        match tokio::task::spawn_blocking(operation).await {
            Ok(outcome) => outcome.map_err(|error| to_agent_error(&error)),
            Err(error) => Err(AgentError {
                code: ErrorCode::SystemFailure as i32,
                message: format!("the sftp operation did not finish: {error}"),
                tool_output: String::new(),
            }),
        }
    }
}

#[tonic::async_trait]
impl<H: SftpHost + 'static> SftpService for SftpServiceImpl<H> {
    /// Creates the account's jail if it is not there, then the login in it.
    async fn create_sftp_user(
        &self,
        request: Request<CreateSftpUserRequest>,
    ) -> Result<Response<CreateSftpUserResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_creation(
            &request.account_username,
            &request.sftp_username,
            &request.password,
        ) {
            Ok(input) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                // Read off the validated input before it is moved, so the
                // response reports the login that was actually created rather
                // than the suffix that was asked for — the two differ by the
                // account prefix the agent applied.
                let created = input.user.as_str().to_owned();

                Self::run(move || sftp::create_sftp_user(host.as_ref(), distro, &input))
                    .await
                    .map(|()| created)
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(sftp_username) => {
                create_sftp_user_response::Result::Ok(CreateSftpUserOk { sftp_username })
            }
            Err(error) => create_sftp_user_response::Result::Error(error),
        };

        Ok(Response::new(CreateSftpUserResponse {
            result: Some(result),
        }))
    }

    /// Sets an existing login's password, which is all there is to set.
    async fn set_sftp_password(
        &self,
        request: Request<SetSftpPasswordRequest>,
    ) -> Result<Response<SetSftpPasswordResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_password_change(
            &request.account_username,
            &request.sftp_username,
            &request.password,
        ) {
            Ok((user, password)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                Self::run(move || sftp::set_sftp_password(host.as_ref(), distro, &user, &password))
                    .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => set_sftp_password_response::Result::Ok(SetSftpPasswordOk {}),
            Err(error) => set_sftp_password_response::Result::Error(error),
        };

        Ok(Response::new(SetSftpPasswordResponse {
            result: Some(result),
        }))
    }

    /// Removes the login, and only the login — never the files it opened.
    async fn delete_sftp_user(
        &self,
        request: Request<DeleteSftpUserRequest>,
    ) -> Result<Response<DeleteSftpUserResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_sftp_user(&request.account_username, &request.sftp_username) {
            Ok((_, user)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                Self::run(move || sftp::delete_sftp_user(host.as_ref(), distro, &user)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => delete_sftp_user_response::Result::Ok(DeleteSftpUserOk {}),
            Err(error) => delete_sftp_user_response::Result::Error(error),
        };

        Ok(Response::new(DeleteSftpUserResponse {
            result: Some(result),
        }))
    }
}
