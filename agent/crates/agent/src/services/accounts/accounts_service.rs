//! `AccountsService`: the operating-system identity behind a hosting account.

use std::sync::Arc;

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::accounts::{AccountError, AccountOperations, SystemHost};
use maran_ops::db::DbHost;
use maran_ops::php::PhpHost;
use maran_ops::sftp::SftpHost;
use tonic::{Request, Response, Status};

use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    AgentError, CreateAccountOk, CreateAccountRequest, CreateAccountResponse, DeleteAccountOk,
    DeleteAccountRequest, DeleteAccountResponse, ErrorCode, GetAccountUsageOk,
    GetAccountUsageRequest, GetAccountUsageResponse, SetAccountQuotaOk, SetAccountQuotaRequest,
    SetAccountQuotaResponse, SuspendAccountOk, SuspendAccountRequest, SuspendAccountResponse,
    UnsuspendAccountOk, UnsuspendAccountRequest, UnsuspendAccountResponse, create_account_response,
    delete_account_response, get_account_usage_response, set_account_quota_response,
    suspend_account_response, unsuspend_account_response,
};
use crate::services::accounts::account_status::to_agent_error;
use crate::services::wire::run_blocking::run_blocking;

/// The noun phrase in the message a failed blocking task reports under.
///
/// Named rather than repeated at each call site so the six rpcs cannot drift
/// into six different wordings for the same breakdown.
const ACCOUNT_OPERATION: &str = "account operation";

/// Serves the account operations over the wire.
///
/// Every rpc follows the same three steps: revalidate the name, run the
/// operation on the blocking pool, and map the outcome into the response's
/// `oneof`. The name check lives once in `Self::validated` and the run in
/// `wire::run_blocking`; a handler is left with only what differs — which
/// operation, and the shape of its `Ok` payload. Failures travel in the
/// payload rather than as a gRPC status, because they are answers the panel
/// acts on — an account that already exists is information, not a transport
/// error.
///
/// # Why every field is an `Arc`
///
/// The operations spawn `useradd`, `setquota`, `quota` and the rest and wait
/// on each, so they run through [`run_blocking`] and not on a runtime worker
/// — a process wait on a worker stalls every other in-flight command, and
/// this service did exactly that until the closures below were moved onto the
/// blocking pool. A `spawn_blocking` closure must own what it uses for
/// `'static`, and a borrow of `&self` is not that; the `Arc`s are what the
/// closures move. The reference counting is not the point and is never
/// contended — one clone per rpc against a process spawn.
pub struct AccountsServiceImpl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> {
    /// The operations, bound to whatever machine they were built against, and
    /// shared with the blocking tasks that run them.
    operations: Arc<AccountOperations<H>>,

    /// The PHP area's machine, because deleting an account must take its
    /// php-fpm pools with it.
    ///
    /// A host on this service rather than a field on `AccountOperations`,
    /// because it is needed by exactly one operation and a constructor argument
    /// every caller must supply for the sake of one method is a dependency the
    /// other five carry for nothing. The two below are here for the same reason.
    php_host: Arc<P>,

    /// The database area's machine, because `userdel` does not touch MySQL: an
    /// account deleted without dropping its databases leaves them for whoever
    /// is given that account name next.
    db_host: Arc<D>,

    /// The SFTP area's machine, because `userdel` does not touch sshd either —
    /// and because the account's home is bind-mounted into a jail that has to
    /// come down before the home does.
    sftp_host: Arc<S>,
}

impl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> AccountsServiceImpl<H, P, D, S> {
    /// Creates the service around `operations` and the three hosts its
    /// deletions need.
    #[must_use]
    pub fn new(operations: AccountOperations<H>, php_host: P, db_host: D, sftp_host: S) -> Self {
        Self {
            operations: Arc::new(operations),
            php_host: Arc::new(php_host),
            db_host: Arc::new(db_host),
            sftp_host: Arc::new(sftp_host),
        }
    }

    /// Revalidates a username arriving from the panel.
    ///
    /// The API validated it already. This is the agent's own check, and it exists
    /// because the agent runs as root and the API does not (rules/security.md
    /// item 1, which requires revalidation in the agent and not only at the API
    /// boundary): a name reaching here becomes a system user, a home directory
    /// and a path segment, so it is checked where it is used.
    ///
    /// # Errors
    ///
    /// Returns the wire error for an invalid name.
    fn validated(username: &str) -> Result<AccountName, AgentError> {
        AccountName::parse(username).map_err(|error| AgentError {
            code: ErrorCode::InvalidInput as i32,
            message: error.to_string(),
            tool_output: String::new(),
        })
    }

    /// Revalidates the username and runs `operation` for it on the blocking
    /// pool.
    ///
    /// The shape every rpc here shares, written once so that adding an rpc
    /// cannot forget the validation step, leave the operation on a runtime
    /// worker, or map an error differently from its neighbours. `operation`
    /// takes the validated name by value and owns everything else it touches,
    /// because it is handed to the runtime and outlives this call.
    ///
    /// # Errors
    ///
    /// Returns the wire error for an invalid name, the [`to_agent_error`]
    /// mapping of whatever the operation failed on, or a system failure when
    /// the blocking task did not finish.
    async fn with_account<T>(
        &self,
        username: &str,
        operation: impl FnOnce(AccountName) -> Result<T, AccountError> + Send + 'static,
    ) -> Result<T, AgentError>
    where
        T: Send + 'static,
    {
        let name = Self::validated(username)?;

        run_blocking(ACCOUNT_OPERATION, to_agent_error, move || operation(name)).await
    }
}

#[tonic::async_trait]
impl<H: SystemHost + 'static, P: PhpHost + 'static, D: DbHost + 'static, S: SftpHost + 'static>
    AccountsService for AccountsServiceImpl<H, P, D, S>
{
    /// Creates the system user, its home directory and its initial quota.
    async fn create_account(
        &self,
        request: Request<CreateAccountRequest>,
    ) -> Result<Response<CreateAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                let quota_bytes = request.quota_bytes;
                move |name| operations.create(&name, quota_bytes)
            })
            .await
        {
            Ok(created) => create_account_response::Result::Ok(CreateAccountOk {
                home_directory: created.home_directory,
                uid: created.uid,
            }),
            Err(error) => create_account_response::Result::Error(error),
        };

        Ok(Response::new(CreateAccountResponse {
            result: Some(result),
        }))
    }

    /// Suspends the account: password locked, shell taken away.
    async fn suspend_account(
        &self,
        request: Request<SuspendAccountRequest>,
    ) -> Result<Response<SuspendAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                move |name| operations.suspend(&name)
            })
            .await
        {
            Ok(()) => suspend_account_response::Result::Ok(SuspendAccountOk {}),
            Err(error) => suspend_account_response::Result::Error(error),
        };

        Ok(Response::new(SuspendAccountResponse {
            result: Some(result),
        }))
    }

    /// Reverses a suspension.
    async fn unsuspend_account(
        &self,
        request: Request<UnsuspendAccountRequest>,
    ) -> Result<Response<UnsuspendAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                move |name| operations.unsuspend(&name)
            })
            .await
        {
            Ok(()) => unsuspend_account_response::Result::Ok(UnsuspendAccountOk {}),
            Err(error) => unsuspend_account_response::Result::Error(error),
        };

        Ok(Response::new(UnsuspendAccountResponse {
            result: Some(result),
        }))
    }

    /// Removes the account's databases, SFTP logins, jail, pools, system user
    /// and everything under its home directory.
    async fn delete_account(
        &self,
        request: Request<DeleteAccountRequest>,
    ) -> Result<Response<DeleteAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                // All three hosts travel in here so the deletion can take the
                // account's databases, its SFTP logins and jail, and its
                // php-fpm pools with it, BEFORE `userdel` removes the user
                // those things name; see `AccountOperations::delete` for why
                // that order is the only safe one, and why leaving any of them
                // behind is the defect that cannot be repaired afterwards.
                // They are cloned rather than borrowed because the closure
                // outlives this call: it runs on the blocking pool.
                let operations = Arc::clone(&self.operations);
                let php_host = Arc::clone(&self.php_host);
                let db_host = Arc::clone(&self.db_host);
                let sftp_host = Arc::clone(&self.sftp_host);
                move |name| {
                    operations.delete(
                        php_host.as_ref(),
                        db_host.as_ref(),
                        sftp_host.as_ref(),
                        &name,
                    )
                }
            })
            .await
        {
            Ok(bytes_freed) => delete_account_response::Result::Ok(DeleteAccountOk { bytes_freed }),
            Err(error) => delete_account_response::Result::Error(error),
        };

        Ok(Response::new(DeleteAccountResponse {
            result: Some(result),
        }))
    }

    /// Replaces the account's disk quota.
    async fn set_account_quota(
        &self,
        request: Request<SetAccountQuotaRequest>,
    ) -> Result<Response<SetAccountQuotaResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                let quota_bytes = request.quota_bytes;
                move |name| operations.set_quota(&name, quota_bytes)
            })
            .await
        {
            Ok(()) => set_account_quota_response::Result::Ok(SetAccountQuotaOk {}),
            Err(error) => set_account_quota_response::Result::Error(error),
        };

        Ok(Response::new(SetAccountQuotaResponse {
            result: Some(result),
        }))
    }

    /// Reads current disk usage and the quota it is measured against.
    async fn get_account_usage(
        &self,
        request: Request<GetAccountUsageRequest>,
    ) -> Result<Response<GetAccountUsageResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                move |name| operations.usage(&name)
            })
            .await
        {
            Ok(usage) => get_account_usage_response::Result::Ok(GetAccountUsageOk {
                used_bytes: usage.used_bytes,
                quota_bytes: usage.quota_bytes,
            }),
            Err(error) => get_account_usage_response::Result::Error(error),
        };

        Ok(Response::new(GetAccountUsageResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/accounts_service_tests.rs"]
mod tests;
