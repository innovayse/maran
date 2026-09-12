//! `FtpsService`: this panel's own vsftpd, and the logins it authorises.

use std::sync::Arc;

use maran_ops::ftps::{self, FtpsHost};
use tonic::{Request, Response, Status};

use crate::proto::ftps_service_server::FtpsService;
use crate::proto::{
    CreateFtpsUserOk, CreateFtpsUserRequest, CreateFtpsUserResponse, DeleteFtpsUserOk,
    DeleteFtpsUserRequest, DeleteFtpsUserResponse, DisableFtpsRequest, DisableFtpsResponse,
    EnableFtpsRequest, EnableFtpsResponse, GetFtpsStatusRequest, GetFtpsStatusResponse,
    ReloadFtpsTlsRequest, ReloadFtpsTlsResponse, SetFtpsPasswordOk, SetFtpsPasswordRequest,
    SetFtpsPasswordResponse, create_ftps_user_response, delete_ftps_user_response,
    disable_ftps_response, enable_ftps_response, get_ftps_status_response,
    reload_ftps_tls_response, set_ftps_password_response,
};
use crate::services::ftps::ftps_status::to_agent_error;
use crate::services::ftps::ftps_status_fields::FtpsStatusFields;
use crate::services::ftps::validated_ftps_configuration::validated_ftps_configuration;
use crate::services::ftps::validated_ftps_creation::validated_ftps_creation;
use crate::services::ftps::validated_ftps_password_change::validated_ftps_password_change;
use crate::services::ftps::validated_ftps_user::validated_ftps_user;
use crate::services::ftps::validated_hostname::validated_hostname;
use crate::services::wire::run_blocking::run_blocking;

/// Serves the FTPS daemon and login operations over the wire.
///
/// Every rpc follows the same three steps: rebuild the request into validated
/// types, run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they are
/// answers the panel acts on — a login that already exists is information, not a
/// transport error (rules/proto.md).
///
/// **No login name arrives here fully qualified and none is forwarded.**
/// `FtpsUserName` has no constructor that takes a whole name, so each login rpc
/// rebuilds the name from the account the panel authorised. Two of the three
/// re-credential or revoke a login, so a forwarded name would be one customer
/// taking over another customer's file access; the boundary is a type rather
/// than a check a handler could forget (see `validated_ftps_user`).
///
/// **The four daemon rpcs answer with the same eight facts**, built through
/// `FtpsStatusFields` so the reading of an observed daemon happens once for all
/// of them.
pub struct FtpsServiceImpl<H> {
    /// The machine the FTPS operations run against.
    host: Arc<H>,
    /// Where platform facts come from — the `useradd`, `userdel`, `chpasswd`,
    /// `getent` and `usermod` paths, the nologin shell, the FTPS group, the
    /// service manager and the unit directory. A service never branches on a
    /// distribution itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: FtpsHost + 'static> FtpsServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: FtpsHost + 'static> FtpsService for FtpsServiceImpl<H> {
    /// Renders, validates and applies the daemon's configuration, then brings it
    /// up — and puts the previous configuration back if it does not answer.
    async fn enable_ftps(
        &self,
        request: Request<EnableFtpsRequest>,
    ) -> Result<Response<EnableFtpsResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ftps_configuration(&request) {
            Ok(configuration) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("ftps operation", to_agent_error, move || {
                    ftps::enable_ftps(host.as_ref(), distro, &configuration)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(state) => enable_ftps_response::Result::Ok(
                FtpsStatusFields::from_state(state).into_enable_ok(),
            ),
            Err(error) => enable_ftps_response::Result::Error(error),
        };

        Ok(Response::new(EnableFtpsResponse {
            result: Some(result),
        }))
    }

    /// Stops the daemon and takes it out of the boot sequence, leaving every
    /// login exactly as it is.
    async fn disable_ftps(
        &self,
        _request: Request<DisableFtpsRequest>,
    ) -> Result<Response<DisableFtpsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("ftps operation", to_agent_error, move || {
            ftps::disable_ftps(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(state) => disable_ftps_response::Result::Ok(
                FtpsStatusFields::from_state(state).into_disable_ok(),
            ),
            Err(error) => disable_ftps_response::Result::Error(error),
        };

        Ok(Response::new(DisableFtpsResponse {
            result: Some(result),
        }))
    }

    /// Reports what the daemon is doing, read back from the file it is started
    /// against rather than from what the panel asked for.
    async fn get_ftps_status(
        &self,
        request: Request<GetFtpsStatusRequest>,
    ) -> Result<Response<GetFtpsStatusResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_hostname(&request.hostname) {
            Ok(hostname) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("ftps status", to_agent_error, move || {
                    ftps::get_ftps_status(host.as_ref(), distro, hostname.as_ref())
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(state) => get_ftps_status_response::Result::Ok(
                FtpsStatusFields::from_state(state).into_status_ok(),
            ),
            Err(error) => get_ftps_status_response::Result::Error(error),
        };

        Ok(Response::new(GetFtpsStatusResponse {
            result: Some(result),
        }))
    }

    /// Restarts the daemon onto certificate material that has been replaced
    /// underneath it. A daemon that is not running is left alone.
    async fn reload_ftps_tls(
        &self,
        _request: Request<ReloadFtpsTlsRequest>,
    ) -> Result<Response<ReloadFtpsTlsResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = run_blocking("ftps operation", to_agent_error, move || {
            ftps::reload_ftps_tls(host.as_ref(), distro)
        })
        .await;

        let result = match result {
            Ok(state) => reload_ftps_tls_response::Result::Ok(
                FtpsStatusFields::from_state(state).into_reload_ok(),
            ),
            Err(error) => reload_ftps_tls_response::Result::Error(error),
        };

        Ok(Response::new(ReloadFtpsTlsResponse {
            result: Some(result),
        }))
    }

    /// Creates the account's jail if it is not there, then the login in it.
    async fn create_ftps_user(
        &self,
        request: Request<CreateFtpsUserRequest>,
    ) -> Result<Response<CreateFtpsUserResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ftps_creation(
            &request.account_username,
            &request.ftps_username,
            &request.password,
        ) {
            Ok(input) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                // Read off the validated input before it is moved, so the
                // response reports the login that was actually created rather
                // than the suffix that was asked for — the two differ by the
                // account prefix the agent applied.
                let created = input.user.as_str().to_owned();

                run_blocking("ftps operation", to_agent_error, move || {
                    ftps::create_ftps_user(host.as_ref(), distro, &input)
                })
                .await
                .map(|()| created)
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(ftps_username) => {
                create_ftps_user_response::Result::Ok(CreateFtpsUserOk { ftps_username })
            }
            Err(error) => create_ftps_user_response::Result::Error(error),
        };

        Ok(Response::new(CreateFtpsUserResponse {
            result: Some(result),
        }))
    }

    /// Sets an existing login's password, which is all there is to set.
    ///
    /// The account travels with the login and is not decoration: the operation
    /// takes the account's lock on it, checks through the account's jail that
    /// the login BELONGS to that account, and re-asserts any suspension it finds
    /// — because `chpasswd` replaces the shadow password field rather than
    /// editing it, so a suspended customer changing their password would
    /// otherwise unlock their own login.
    async fn set_ftps_password(
        &self,
        request: Request<SetFtpsPasswordRequest>,
    ) -> Result<Response<SetFtpsPasswordResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ftps_password_change(
            &request.account_username,
            &request.ftps_username,
            &request.password,
        ) {
            Ok((account, user, password)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("ftps operation", to_agent_error, move || {
                    ftps::set_ftps_password(host.as_ref(), distro, &account, &user, &password)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => set_ftps_password_response::Result::Ok(SetFtpsPasswordOk {}),
            Err(error) => set_ftps_password_response::Result::Error(error),
        };

        Ok(Response::new(SetFtpsPasswordResponse {
            result: Some(result),
        }))
    }

    /// Removes the login, and only the login — never the files it opened, and
    /// never the jail the account's other logins are still chrooted into.
    ///
    /// The account is PASSED ON and not discarded, and that is the whole
    /// authorisation of a destructive rpc: `<account>_<name>` has no unique
    /// decomposition when account names may carry the separator, so the login
    /// `bob` of account `alice` and the hosting account `alice_bob`'s own system
    /// user are the same nine characters. The operation checks, under the
    /// account's lock, that the named entry's passwd home is this account's
    /// jail; a handler that dropped the account would be asking `userdel` to
    /// remove whichever entry answered to the name.
    async fn delete_ftps_user(
        &self,
        request: Request<DeleteFtpsUserRequest>,
    ) -> Result<Response<DeleteFtpsUserResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_ftps_user(&request.account_username, &request.ftps_username) {
            Ok((account, user)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("ftps operation", to_agent_error, move || {
                    ftps::delete_ftps_user(host.as_ref(), distro, &account, &user)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => delete_ftps_user_response::Result::Ok(DeleteFtpsUserOk {}),
            Err(error) => delete_ftps_user_response::Result::Error(error),
        };

        Ok(Response::new(DeleteFtpsUserResponse {
            result: Some(result),
        }))
    }
}
