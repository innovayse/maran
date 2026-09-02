//! `AccountsService`: the operating-system identity behind a hosting account.

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

/// Serves the account operations over the wire.
///
/// Every rpc follows the same three steps: revalidate the name, run the operation,
/// and map the outcome into the response's `oneof`. The first two steps and the
/// failure half of the third live once in `Self::run`; a handler is left with
/// only what differs — which operation, and the shape of its `Ok` payload.
/// Failures travel in the payload rather than as a gRPC status, because they are
/// answers the panel acts on — an account that already exists is information, not
/// a transport error.
pub struct AccountsServiceImpl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> {
    /// The operations, bound to whatever machine they were built against.
    operations: AccountOperations<H>,

    /// The PHP area's machine, because deleting an account must take its
    /// php-fpm pools with it.
    ///
    /// A host on this service rather than a field on `AccountOperations`,
    /// because it is needed by exactly one operation and a constructor argument
    /// every caller must supply for the sake of one method is a dependency the
    /// other five carry for nothing. The two below are here for the same reason.
    php_host: P,

    /// The database area's machine, because `userdel` does not touch MySQL: an
    /// account deleted without dropping its databases leaves them for whoever
    /// is given that account name next.
    db_host: D,

    /// The SFTP area's machine, because `userdel` does not touch sshd either —
    /// and because the account's home is bind-mounted into a jail that has to
    /// come down before the home does.
    sftp_host: S,
}

impl<H: SystemHost, P: PhpHost, D: DbHost, S: SftpHost> AccountsServiceImpl<H, P, D, S> {
    /// Creates the service around `operations` and the three hosts its
    /// deletions need.
    #[must_use]
    pub fn new(operations: AccountOperations<H>, php_host: P, db_host: D, sftp_host: S) -> Self {
        Self {
            operations,
            php_host,
            db_host,
            sftp_host,
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

    /// Runs one operation for the username the panel sent.
    ///
    /// Revalidates the name, hands the validated [`AccountName`] to `operation`,
    /// and maps an operation failure onto the wire error — the shape every rpc
    /// shares, written once so that adding an rpc cannot forget the validation
    /// step or map an error differently from its neighbours.
    ///
    /// # Errors
    ///
    /// Returns the wire error for an invalid name, or the [`to_agent_error`]
    /// mapping of whatever the operation failed on.
    fn run<T>(
        username: &str,
        operation: impl FnOnce(&AccountName) -> Result<T, AccountError>,
    ) -> Result<T, AgentError> {
        let name = Self::validated(username)?;
        operation(&name).map_err(|error| to_agent_error(&error))
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
        let result = match Self::run(&request.username, |name| {
            self.operations.create(name, request.quota_bytes)
        }) {
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
        let result = match Self::run(&request.username, |name| self.operations.suspend(name)) {
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
        let result = match Self::run(&request.username, |name| self.operations.unsuspend(name)) {
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
        let result = match Self::run(&request.username, |name| {
            // All three hosts travel in here so the deletion can take the
            // account's databases, its SFTP logins and jail, and its php-fpm
            // pools with it, BEFORE `userdel` removes the user those things
            // name; see `AccountOperations::delete` for why that order is the
            // only safe one, and why leaving any of them behind is the defect
            // that cannot be repaired afterwards.
            self.operations
                .delete(&self.php_host, &self.db_host, &self.sftp_host, name)
        }) {
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
        let result = match Self::run(&request.username, |name| {
            self.operations.set_quota(name, request.quota_bytes)
        }) {
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
        let result = match Self::run(&request.username, |name| self.operations.usage(name)) {
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
