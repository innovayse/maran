//! `AccountsService`: the operating-system identity behind a hosting account.

use maran_agent_core::validation::name::AccountName;
use maran_ops::accounts::{AccountOperations, SystemHost};
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
/// and map the outcome into the response's `oneof`. Failures travel in the payload
/// rather than as a gRPC status, because they are answers the panel acts on — an
/// account that already exists is information, not a transport error.
pub struct AccountsServiceImpl<H: SystemHost> {
    /// The operations, bound to whatever machine they were built against.
    operations: AccountOperations<H>,
}

impl<H: SystemHost> AccountsServiceImpl<H> {
    /// Creates the service around `operations`.
    #[must_use]
    pub fn new(operations: AccountOperations<H>) -> Self {
        Self { operations }
    }

    /// Revalidates a username arriving from the panel.
    ///
    /// The API validated it already. This is the agent's own check, and it exists
    /// because the agent runs as root and the API does not (rules/security.md
    /// "Undistrust of the caller"): a name reaching here becomes a system user, a
    /// home directory and a path segment, so it is checked where it is used.
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
}

#[tonic::async_trait]
impl<H: SystemHost + 'static> AccountsService for AccountsServiceImpl<H> {
    /// Creates the system user, its home directory and its initial quota.
    async fn create_account(
        &self,
        request: Request<CreateAccountRequest>,
    ) -> Result<Response<CreateAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.create(&name, request.quota_bytes) {
                Ok(created) => create_account_response::Result::Ok(CreateAccountOk {
                    home_directory: created.home_directory,
                    uid: created.uid,
                }),
                Err(error) => create_account_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => create_account_response::Result::Error(invalid),
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
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.suspend(&name) {
                Ok(()) => suspend_account_response::Result::Ok(SuspendAccountOk {}),
                Err(error) => suspend_account_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => suspend_account_response::Result::Error(invalid),
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
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.unsuspend(&name) {
                Ok(()) => unsuspend_account_response::Result::Ok(UnsuspendAccountOk {}),
                Err(error) => unsuspend_account_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => unsuspend_account_response::Result::Error(invalid),
        };

        Ok(Response::new(UnsuspendAccountResponse {
            result: Some(result),
        }))
    }

    /// Removes the system user and everything under its home directory.
    async fn delete_account(
        &self,
        request: Request<DeleteAccountRequest>,
    ) -> Result<Response<DeleteAccountResponse>, Status> {
        let request = request.into_inner();
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.delete(&name) {
                Ok(bytes_freed) => {
                    delete_account_response::Result::Ok(DeleteAccountOk { bytes_freed })
                }
                Err(error) => delete_account_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => delete_account_response::Result::Error(invalid),
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
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.set_quota(&name, request.quota_bytes) {
                Ok(()) => set_account_quota_response::Result::Ok(SetAccountQuotaOk {}),
                Err(error) => set_account_quota_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => set_account_quota_response::Result::Error(invalid),
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
        let result = match Self::validated(&request.username) {
            Ok(name) => match self.operations.usage(&name) {
                Ok(usage) => get_account_usage_response::Result::Ok(GetAccountUsageOk {
                    used_bytes: usage.used_bytes,
                    quota_bytes: usage.quota_bytes,
                }),
                Err(error) => get_account_usage_response::Result::Error(to_agent_error(&error)),
            },
            Err(invalid) => get_account_usage_response::Result::Error(invalid),
        };

        Ok(Response::new(GetAccountUsageResponse {
            result: Some(result),
        }))
    }
}
