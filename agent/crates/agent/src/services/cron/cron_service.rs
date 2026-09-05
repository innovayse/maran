//! `CronService`: the scheduled tasks a hosting account owns.

use std::sync::Arc;

use maran_distro::DistroAdapter;
use maran_ops::cron::{self, CronError, CronHost};
use tonic::{Request, Response, Status};

use crate::proto::cron_service_server::CronService;
use crate::proto::{
    AgentError, CreateCronEntryOk, CreateCronEntryRequest, CreateCronEntryResponse,
    CronEnvironmentVariable, DeleteCronEntryOk, DeleteCronEntryRequest, DeleteCronEntryResponse,
    ErrorCode, GetCronEntryOutputRequest, GetCronEntryOutputResponse, GetCronEnvironmentOk,
    GetCronEnvironmentRequest, GetCronEnvironmentResponse, ListCronEntriesOk,
    ListCronEntriesRequest, ListCronEntriesResponse, SetCronEntryEnabledOk,
    SetCronEntryEnabledRequest, SetCronEntryEnabledResponse, SetCronEnvironmentOk,
    SetCronEnvironmentRequest, SetCronEnvironmentResponse, UpdateCronEntryOk,
    UpdateCronEntryRequest, UpdateCronEntryResponse, create_cron_entry_response,
    delete_cron_entry_response, get_cron_entry_output_response, get_cron_environment_response,
    list_cron_entries_response, set_cron_entry_enabled_response, set_cron_environment_response,
    update_cron_entry_response,
};
use crate::services::cron::cron_status::to_agent_error;
use crate::services::cron::entry_output::entry_output;
use crate::services::cron::listed_entry::listed_entry;
use crate::services::cron::validated_account::validated_account;
use crate::services::cron::validated_creation::validated_creation;
use crate::services::cron::validated_entry::validated_entry;
use crate::services::cron::validated_environment::validated_environment;
use crate::services::cron::validated_update::validated_update;

/// Serves the per-account cron operations over the wire.
///
/// Every rpc follows the same three steps: rebuild the request into validated
/// types, run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — an entry that is already there is
/// information, not a transport error (rules/proto.md).
///
/// The distro adapter is held because five of these operations render a crontab
/// and the rendered line names the interpreter by absolute path, which is a
/// platform fact. The service asks no question of it itself and branches on
/// nothing: it hands it to the operation, which is where the answer is used.
pub struct CronServiceImpl<H> {
    /// The machine the cron operations read and write.
    host: Arc<H>,
    /// Where the interpreter a crontab line names lives on this family.
    distro: &'static dyn DistroAdapter,
}

impl<H: CronHost + 'static> CronServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn DistroAdapter) -> Self {
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
    /// Every operation here spawns `crontab(1)` and waits for it, or forks into
    /// the account to touch a file under its home; rules/rust.md requires both
    /// off the runtime's workers, since a process wait on a worker stalls every
    /// other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns the [`to_agent_error`] mapping of whatever the operation failed
    /// on, or a system failure when the blocking task did not finish — a panic
    /// inside the agent has no domain answer to give, and rules/proto.md
    /// reserves gRPC statuses for transport problems, which it is not.
    async fn run<T, F>(operation: F) -> Result<T, AgentError>
    where
        F: FnOnce() -> Result<T, CronError> + Send + 'static,
        T: Send + 'static,
    {
        match tokio::task::spawn_blocking(operation).await {
            Ok(outcome) => outcome.map_err(|error| to_agent_error(&error)),
            Err(error) => Err(AgentError {
                code: ErrorCode::SystemFailure as i32,
                message: format!("the cron operation did not finish: {error}"),
                tool_output: String::new(),
            }),
        }
    }
}

#[tonic::async_trait]
impl<H: CronHost + 'static> CronService for CronServiceImpl<H> {
    /// Lists the entries the agent manages in the account's crontab.
    async fn list_cron_entries(
        &self,
        request: Request<ListCronEntriesRequest>,
    ) -> Result<Response<ListCronEntriesResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_account(&request.account_username) {
            Ok(account) => {
                let host = Arc::clone(&self.host);
                Self::run(move || cron::list_cron_entries(host.as_ref(), &account)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(entries) => list_cron_entries_response::Result::Ok(ListCronEntriesOk {
                entries: entries.into_iter().map(listed_entry).collect(),
            }),
            Err(error) => list_cron_entries_response::Result::Error(error),
        };

        Ok(Response::new(ListCronEntriesResponse {
            result: Some(result),
        }))
    }

    /// Installs a new entry, and returns the id the agent minted for it.
    async fn create_cron_entry(
        &self,
        request: Request<CreateCronEntryRequest>,
    ) -> Result<Response<CreateCronEntryResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_creation(
            &request.account_username,
            request.schedule.as_ref(),
            &request.command,
        ) {
            Ok((account, schedule, command)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                Self::run(move || {
                    cron::create_cron_entry(host.as_ref(), distro, &account, &schedule, &command)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(entry) => create_cron_entry_response::Result::Ok(CreateCronEntryOk {
                entry_id: entry.as_str().to_owned(),
            }),
            Err(error) => create_cron_entry_response::Result::Error(error),
        };

        Ok(Response::new(CreateCronEntryResponse {
            result: Some(result),
        }))
    }

    /// Replaces an entry's schedule and command, leaving its enablement alone.
    async fn update_cron_entry(
        &self,
        request: Request<UpdateCronEntryRequest>,
    ) -> Result<Response<UpdateCronEntryResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_update(
            &request.account_username,
            &request.entry_id,
            request.schedule.as_ref(),
            &request.command,
        ) {
            Ok((account, entry, schedule, command)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                Self::run(move || {
                    cron::update_cron_entry(
                        host.as_ref(),
                        distro,
                        &account,
                        &entry,
                        &schedule,
                        &command,
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => update_cron_entry_response::Result::Ok(UpdateCronEntryOk {}),
            Err(error) => update_cron_entry_response::Result::Error(error),
        };

        Ok(Response::new(UpdateCronEntryResponse {
            result: Some(result),
        }))
    }

    /// Removes an entry from the crontab and takes its files with it.
    async fn delete_cron_entry(
        &self,
        request: Request<DeleteCronEntryRequest>,
    ) -> Result<Response<DeleteCronEntryResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_entry(&request.account_username, &request.entry_id) {
            Ok((account, entry)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                Self::run(move || cron::delete_cron_entry(host.as_ref(), distro, &account, &entry))
                    .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => delete_cron_entry_response::Result::Ok(DeleteCronEntryOk {}),
            Err(error) => delete_cron_entry_response::Result::Error(error),
        };

        Ok(Response::new(DeleteCronEntryResponse {
            result: Some(result),
        }))
    }

    /// Switches an entry on or off without touching what it runs.
    async fn set_cron_entry_enabled(
        &self,
        request: Request<SetCronEntryEnabledRequest>,
    ) -> Result<Response<SetCronEntryEnabledResponse>, Status> {
        let request = request.into_inner();
        let enabled = request.enabled;

        let result = match validated_entry(&request.account_username, &request.entry_id) {
            Ok((account, entry)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                Self::run(move || {
                    cron::set_cron_entry_enabled(host.as_ref(), distro, &account, &entry, enabled)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => set_cron_entry_enabled_response::Result::Ok(SetCronEntryEnabledOk {}),
            Err(error) => set_cron_entry_enabled_response::Result::Error(error),
        };

        Ok(Response::new(SetCronEntryEnabledResponse {
            result: Some(result),
        }))
    }

    /// Reads what an entry's most recent run left behind.
    async fn get_cron_entry_output(
        &self,
        request: Request<GetCronEntryOutputRequest>,
    ) -> Result<Response<GetCronEntryOutputResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_entry(&request.account_username, &request.entry_id) {
            Ok((account, entry)) => {
                let host = Arc::clone(&self.host);
                Self::run(move || cron::get_cron_entry_output(host.as_ref(), &account, &entry))
                    .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(output) => get_cron_entry_output_response::Result::Ok(entry_output(output)),
            Err(error) => get_cron_entry_output_response::Result::Error(error),
        };

        Ok(Response::new(GetCronEntryOutputResponse {
            result: Some(result),
        }))
    }

    /// Reads the environment assignments the agent manages for the account.
    async fn get_cron_environment(
        &self,
        request: Request<GetCronEnvironmentRequest>,
    ) -> Result<Response<GetCronEnvironmentResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_account(&request.account_username) {
            Ok(account) => {
                let host = Arc::clone(&self.host);
                Self::run(move || cron::get_cron_environment(host.as_ref(), &account)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(environment) => get_cron_environment_response::Result::Ok(GetCronEnvironmentOk {
                variables: environment
                    .into_iter()
                    .map(|variable| CronEnvironmentVariable {
                        name: variable.name.as_str().to_owned(),
                        value: variable.value.as_str().to_owned(),
                    })
                    .collect(),
            }),
            Err(error) => get_cron_environment_response::Result::Error(error),
        };

        Ok(Response::new(GetCronEnvironmentResponse {
            result: Some(result),
        }))
    }

    /// Replaces the agent-managed environment assignments, whole.
    async fn set_cron_environment(
        &self,
        request: Request<SetCronEnvironmentRequest>,
    ) -> Result<Response<SetCronEnvironmentResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_environment(&request.account_username, &request.variables) {
            Ok((account, environment)) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;

                Self::run(move || {
                    cron::set_cron_environment(host.as_ref(), distro, &account, environment)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => set_cron_environment_response::Result::Ok(SetCronEnvironmentOk {}),
            Err(error) => set_cron_environment_response::Result::Error(error),
        };

        Ok(Response::new(SetCronEnvironmentResponse {
            result: Some(result),
        }))
    }
}
