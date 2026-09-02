//! `DbService`: customer databases and the dedicated user each one is made with.

use std::sync::Arc;

use maran_ops::db::{self, DbError, DbHost};
use tonic::{Request, Response, Status};

use crate::proto::db_service_server::DbService;
use crate::proto::{
    AgentError, CreateDatabaseOk, CreateDatabaseRequest, CreateDatabaseResponse, DatabaseInfo,
    DropDatabaseOk, DropDatabaseRequest, DropDatabaseResponse, ErrorCode, GetDatabaseSizeOk,
    GetDatabaseSizeRequest, GetDatabaseSizeResponse, ListDatabasesOk, ListDatabasesRequest,
    ListDatabasesResponse, SetDatabasePasswordOk, SetDatabasePasswordRequest,
    SetDatabasePasswordResponse, create_database_response, drop_database_response,
    get_database_size_response, list_databases_response, set_database_password_response,
};
use crate::services::db::db_status::to_agent_error;
use crate::services::db::validated_account::validated_account;
use crate::services::db::validated_creation::validated_creation;
use crate::services::db::validated_database::validated_database;
use crate::services::db::validated_password_change::validated_password_change;
use crate::services::db::validated_removal::validated_removal;

/// Serves the database operations over the wire.
///
/// Every rpc follows the same three steps: rebuild the request into validated
/// types, run one operation, and map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they
/// are answers the panel acts on — a database that is already there is
/// information, not a transport error (rules/proto.md).
///
/// **No name arrives here fully qualified and none is forwarded.** The two name
/// types have no constructor that takes a whole name, so each rpc rebuilds the
/// name from the account the panel authorised. A request naming another
/// tenant's database therefore produces a name under the caller's own account
/// rather than a refusal to be got round — the boundary is a type, not a check
/// (see `validated_database`).
///
/// There is no distro adapter here, and that absence is deliberate. The one
/// platform fact this area needs is where the client binary lives, and
/// `ProcessDbHost` takes it at construction; a service that also held an
/// adapter would suggest an rpc branches on a distribution, and none does.
pub struct DbServiceImpl<H> {
    /// The server the database operations run against.
    host: Arc<H>,
}

impl<H: DbHost + 'static> DbServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H) -> Self {
        Self {
            host: Arc::new(host),
        }
    }

    /// Runs one operation on the blocking pool and maps its failure onto the
    /// wire error — the shape every rpc here shares, written once so that
    /// adding an rpc cannot forget to leave the runtime or map an error
    /// differently from its neighbours.
    ///
    /// Every operation here spawns the database client and waits for it;
    /// rules/rust.md requires that off the runtime's workers, since a process
    /// wait on a worker stalls every other in-flight command.
    ///
    /// # Errors
    ///
    /// Returns the [`to_agent_error`] mapping of whatever the operation failed
    /// on, or a system failure when the blocking task did not finish — a panic
    /// inside the agent has no domain answer to give, and rules/proto.md
    /// reserves gRPC statuses for transport problems, which it is not.
    async fn run<T, F>(operation: F) -> Result<T, AgentError>
    where
        F: FnOnce() -> Result<T, DbError> + Send + 'static,
        T: Send + 'static,
    {
        match tokio::task::spawn_blocking(operation).await {
            Ok(outcome) => outcome.map_err(|error| to_agent_error(&error)),
            Err(error) => Err(AgentError {
                code: ErrorCode::SystemFailure as i32,
                message: format!("the database operation did not finish: {error}"),
                tool_output: String::new(),
            }),
        }
    }
}

#[tonic::async_trait]
impl<H: DbHost + 'static> DbService for DbServiceImpl<H> {
    /// Creates the database, its dedicated user, and that user's scoped grant.
    async fn create_database(
        &self,
        request: Request<CreateDatabaseRequest>,
    ) -> Result<Response<CreateDatabaseResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_creation(
            &request.account_username,
            &request.database_name,
            &request.db_username,
            &request.password,
        ) {
            Ok(input) => {
                let host = Arc::clone(&self.host);
                // The created names are read off the validated input before it
                // is moved, so the response reports what was actually built
                // rather than what the request asked for — the two differ by
                // the account prefix the agent applied.
                let created = (
                    input.database.as_str().to_owned(),
                    input.user.as_str().to_owned(),
                );

                Self::run(move || db::create_database(host.as_ref(), &input))
                    .await
                    .map(|()| created)
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok((database_name, db_username)) => {
                create_database_response::Result::Ok(CreateDatabaseOk {
                    database_name,
                    db_username,
                })
            }
            Err(error) => create_database_response::Result::Error(error),
        };

        Ok(Response::new(CreateDatabaseResponse {
            result: Some(result),
        }))
    }

    /// Drops the database and the user it was created with.
    async fn drop_database(
        &self,
        request: Request<DropDatabaseRequest>,
    ) -> Result<Response<DropDatabaseResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_removal(
            &request.account_username,
            &request.database_name,
            &request.db_username,
        ) {
            Ok((database, user)) => {
                let host = Arc::clone(&self.host);
                Self::run(move || db::drop_database(host.as_ref(), &database, &user)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => drop_database_response::Result::Ok(DropDatabaseOk {}),
            Err(error) => drop_database_response::Result::Error(error),
        };

        Ok(Response::new(DropDatabaseResponse {
            result: Some(result),
        }))
    }

    /// Sets an existing database user's password, and nothing else.
    ///
    /// The whole of the recovery path for a lost credential: nobody keeps a
    /// copy, and `create_database` deliberately leaves an existing pair's
    /// password alone so that retrying a creation is safe (`db.proto`).
    async fn set_database_password(
        &self,
        request: Request<SetDatabasePasswordRequest>,
    ) -> Result<Response<SetDatabasePasswordResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_password_change(
            &request.account_username,
            &request.db_username,
            &request.password,
        ) {
            Ok((user, password)) => {
                let host = Arc::clone(&self.host);
                Self::run(move || db::set_database_password(host.as_ref(), &user, &password)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => set_database_password_response::Result::Ok(SetDatabasePasswordOk {}),
            Err(error) => set_database_password_response::Result::Error(error),
        };

        Ok(Response::new(SetDatabasePasswordResponse {
            result: Some(result),
        }))
    }

    /// Lists the databases on this server whose names decode to the account.
    async fn list_databases(
        &self,
        request: Request<ListDatabasesRequest>,
    ) -> Result<Response<ListDatabasesResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_account(&request.account_username) {
            Ok(account) => {
                let host = Arc::clone(&self.host);
                Self::run(move || db::list_databases(host.as_ref(), &account)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(databases) => list_databases_response::Result::Ok(ListDatabasesOk {
                databases: databases
                    .into_iter()
                    .map(|summary| DatabaseInfo {
                        database_name: summary.name.as_str().to_owned(),
                        // Both left UNSET, not defaulted, and `db.proto` gives
                        // them explicit presence so that absence can be said.
                        // The listing establishes neither: the server records
                        // which users are GRANTED on a database rather than
                        // which one was "its" user, and a size is a separate
                        // query per database that would turn an overview into a
                        // full scan of the server's table metadata. A service
                        // may not go and find out either — that would be
                        // business work in a translation layer (rules/rust.md
                        // "Service anatomy") — and an empty string or a `0`
                        // would be a claim the agent cannot support.
                        db_username: None,
                        size_bytes: None,
                    })
                    .collect(),
            }),
            Err(error) => list_databases_response::Result::Error(error),
        };

        Ok(Response::new(ListDatabasesResponse {
            result: Some(result),
        }))
    }

    /// Reads one database's size, as the server accounts for it.
    async fn get_database_size(
        &self,
        request: Request<GetDatabaseSizeRequest>,
    ) -> Result<Response<GetDatabaseSizeResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_database(&request.account_username, &request.database_name) {
            Ok(database) => {
                let host = Arc::clone(&self.host);
                Self::run(move || db::database_size(host.as_ref(), &database)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(report) => get_database_size_response::Result::Ok(GetDatabaseSizeOk {
                size_bytes: report.bytes,
            }),
            Err(error) => get_database_size_response::Result::Error(error),
        };

        Ok(Response::new(GetDatabaseSizeResponse {
            result: Some(result),
        }))
    }
}
