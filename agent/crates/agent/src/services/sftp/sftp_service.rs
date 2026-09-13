//! `SftpService`: chrooted OpenSSH file-transfer logins for a hosting account.

use std::sync::Arc;

use maran_ops::logins::{self, LoginsHost};
use maran_ops::sftp::{self, SftpHost};
use tonic::{Request, Response, Status};

use crate::proto::sftp_service_server::SftpService;
use crate::proto::{
    CreateSftpUserOk, CreateSftpUserRequest, CreateSftpUserResponse, DeleteSftpUserOk,
    DeleteSftpUserRequest, DeleteSftpUserResponse, SetAccountLoginsLockedOk,
    SetAccountLoginsLockedRequest, SetAccountLoginsLockedResponse, SetSftpPasswordOk,
    SetSftpPasswordRequest, SetSftpPasswordResponse, create_sftp_user_response,
    delete_sftp_user_response, set_account_logins_locked_response, set_sftp_password_response,
};
use crate::services::sftp::logins_status::to_agent_error as to_logins_error;
use crate::services::sftp::sftp_status::to_agent_error;
use crate::services::sftp::validated_creation::validated_creation;
use crate::services::sftp::validated_password_change::validated_password_change;
use crate::services::sftp::validated_sftp_user::validated_sftp_user;
use crate::services::wire::run_blocking::run_blocking;
use crate::services::wire::validated_account::validated_account;

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
pub struct SftpServiceImpl<H, L> {
    /// The machine the SFTP operations run against.
    host: Arc<H>,
    /// The machine the LOGIN operations run against.
    ///
    /// A second host and not a method on the first, because the enumeration a
    /// suspension acts on is not an SFTP question: an account can hold logins
    /// of two protocols, and a seam owned by one of them answers about one of
    /// them. That is the defect `ops::logins` was made to close, and giving
    /// this service one host for both would put it back.
    logins_host: Arc<L>,
    /// Where platform facts come from — the `useradd`, `userdel` and `chpasswd`
    /// paths, the nologin shell, the chroot group and the unit directory. A
    /// service never branches on a distribution itself (rules/rust.md "Distro
    /// adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: SftpHost + 'static, L: LoginsHost + 'static> SftpServiceImpl<H, L> {
    /// Creates the service around the hosts it runs operations against.
    #[must_use]
    pub fn new(host: H, logins_host: L, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            logins_host: Arc::new(logins_host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: SftpHost + 'static, L: LoginsHost + 'static> SftpService for SftpServiceImpl<H, L> {
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

                run_blocking("sftp operation", to_agent_error, move || {
                    sftp::create_sftp_user(host.as_ref(), distro, &input)
                })
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
            Ok((account, user, password)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("sftp operation", to_agent_error, move || {
                    sftp::set_sftp_password(host.as_ref(), distro, &account, &user, &password)
                })
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
    ///
    /// The ACCOUNT is passed through beside the login and not discarded. It is
    /// what the operation takes its per-account lock on and what the login's
    /// jail is derived from for the ownership check: an account name may contain
    /// the separator, so the login `bob` of account `alice` and the hosting
    /// account `alice_bob` are one string, and only the account the panel
    /// authorised tells them apart. A handler that dropped it turned this rpc
    /// into a way to `userdel` a neighbouring tenant.
    async fn delete_sftp_user(
        &self,
        request: Request<DeleteSftpUserRequest>,
    ) -> Result<Response<DeleteSftpUserResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_sftp_user(&request.account_username, &request.sftp_username) {
            Ok((account, user)) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("sftp operation", to_agent_error, move || {
                    sftp::delete_sftp_user(host.as_ref(), distro, &account, &user)
                })
                .await
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

    /// Locks, or unlocks, every login the account holds, because the account
    /// itself was suspended or resumed.
    ///
    /// It takes an ACCOUNT and no login name, and that is deliberate: the
    /// logins come from the host's own password database rather than from a
    /// list the panel supplies, because a list can only describe what the panel
    /// remembers creating — and a login it has forgotten is exactly the one
    /// that would keep letting a suspended customer in.
    ///
    /// It reaches `ops::logins` and not `ops::sftp`, and that is the same
    /// sentence one protocol further on: every file-transfer login the account
    /// holds is turned here, whichever daemon serves it. An enumeration owned
    /// by the SFTP area would walk past an FTPS login and report success.
    ///
    /// On the LOCKING direction it also ends every session the account already
    /// has open, and answers with how many processes were signalled. That number
    /// is the only thing on this response: it is an event nothing can re-read, so
    /// an attestation that omitted it would be an operator's record of a
    /// suspension that did one privileged thing more than the record said.
    async fn set_account_logins_locked(
        &self,
        request: Request<SetAccountLoginsLockedRequest>,
    ) -> Result<Response<SetAccountLoginsLockedResponse>, Status> {
        let request = request.into_inner();
        let locked = request.locked;

        let result = match validated_account(&request.account_username) {
            Ok(account) => {
                let (host, distro) = (Arc::clone(&self.logins_host), self.distro);
                run_blocking("login operation", to_logins_error, move || {
                    logins::set_account_logins_locked(host.as_ref(), distro, &account, locked)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            // Only the cull count crosses here. The login set the operation also
            // answers with does NOT: the panel reads those same facts through
            // `GetAccountSuspensionState`, which observes the host after the
            // fact, and duplicating them on this response would give the panel
            // two answers to one question and no rule for which to believe.
            //
            // The count is the opposite case — it is an EVENT and not a state,
            // so nothing can re-read it afterwards, and before this field it
            // reached only the agent's log while the panel's own confirmation
            // dialog was already promising the operator that a running transfer
            // gets cut. `sessions_ended` is `Option<u32>` end to end, so the
            // unlock direction (which culls nothing) stays absent rather than
            // sending a zero that would read as a measurement.
            Ok(outcome) => {
                set_account_logins_locked_response::Result::Ok(SetAccountLoginsLockedOk {
                    sessions_ended: outcome.sessions_ended,
                })
            }
            Err(error) => set_account_logins_locked_response::Result::Error(error),
        };

        Ok(Response::new(SetAccountLoginsLockedResponse {
            result: Some(result),
        }))
    }
}
