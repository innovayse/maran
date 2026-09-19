//! `AccountsService`: the operating-system identity behind a hosting account.

use std::sync::Arc;

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::accounts::{AccountError, AccountOperations, SystemHost};
use maran_ops::cron::CronHost;
use maran_ops::db::DbHost;
use maran_ops::ftps::FtpsHost;
use maran_ops::logins::LoginsHost;
use maran_ops::php::PhpHost;
use maran_ops::sftp::SftpHost;
use maran_ops::sites::SiteHost;
use tonic::{Request, Response, Status};

use crate::proto::accounts_service_server::AccountsService;
use crate::proto::{
    AgentError, CreateAccountOk, CreateAccountRequest, CreateAccountResponse, DeleteAccountOk,
    DeleteAccountRequest, DeleteAccountResponse, ErrorCode, GetAccountSuspensionStateOk,
    GetAccountSuspensionStateRequest, GetAccountSuspensionStateResponse, GetAccountUsageOk,
    GetAccountUsageRequest, GetAccountUsageResponse, RepairAccountHomeGroupsRequest,
    RepairAccountHomeGroupsResponse, SetAccountQuotaOk, SetAccountQuotaRequest,
    SetAccountQuotaResponse, SftpLoginSuspensionFact, SiteSuspensionFact, SuspendAccountOk,
    SuspendAccountRequest, SuspendAccountResponse, UnsuspendAccountOk, UnsuspendAccountRequest,
    UnsuspendAccountResponse, create_account_response, delete_account_response,
    get_account_suspension_state_response, get_account_usage_response,
    repair_account_home_groups_response, set_account_quota_response, suspend_account_response,
    unsuspend_account_response,
};
use crate::services::accounts::account_status::to_agent_error;
use crate::services::accounts::to_login_password_state::to_login_password_state;
use crate::services::accounts::to_transfer_protocol::to_transfer_protocol;
use crate::services::accounts::wire_home_group_repair_report::wire_home_group_repair_report;
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
pub struct AccountsServiceImpl<
    H: SystemHost,
    P: PhpHost,
    D: DbHost,
    S: SftpHost,
    F: FtpsHost,
    W: SiteHost,
    C: CronHost,
    L: LoginsHost,
> {
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

    /// The FTPS area's machine, because `userdel` does not touch vsftpd either
    /// — and because the account's home is bind-mounted into a SECOND jail,
    /// under a different root, that has to come down before the home does.
    ///
    /// A host of its own beside `sftp_host` rather than the same one, for the
    /// reason `logins_host` is separate: each area enumerates the passwd
    /// database filtered by its own jail directory, so a teardown reached
    /// through the SFTP seam removes SFTP logins and leaves every FTPS login,
    /// its mount and its jail standing.
    ftps_host: Arc<F>,

    /// The site area's machine, because a suspension the panel cannot OBSERVE
    /// is one it must not report. Reading a vhost is the only thing this
    /// service asks of it, and it never writes one: the vhost a suspended
    /// account serves is written by `SitesService.DisableSite`, driven per
    /// site by the panel, which is the only party that knows which sites the
    /// customer had enabled.
    site_host: Arc<W>,

    /// The cron area's machine, because a suspension the panel cannot OBSERVE
    /// is one it must not report — and cron is the subsystem where that is
    /// sharpest. The Cron module keeps no rows at all, so the only place the
    /// answer exists is the crontab on this host; a check that asked the panel
    /// would be green over a firing table.
    ///
    /// Read-only here, exactly as `site_host` is: the crontab a suspended
    /// account carries is written by `CronService.SetAccountCronSuspended`.
    cron_host: Arc<C>,

    /// The login area's machine, because the credentials a suspension has to
    /// have turned are passwd entries of their own — and because there are two
    /// protocols of them.
    ///
    /// A host of its own beside `sftp_host` rather than the same one, and the
    /// separation is the point: an enumeration reached through the SFTP seam
    /// answers about SFTP, so an FTPS login would be neither locked nor
    /// reported while the attestation claimed to cover every login.
    ///
    /// Read-only here: the locking itself is `SftpService`'s
    /// `SetAccountLoginsLocked`, driven by the panel.
    logins_host: Arc<L>,
}

impl<
    H: SystemHost,
    P: PhpHost,
    D: DbHost,
    S: SftpHost,
    F: FtpsHost,
    W: SiteHost,
    C: CronHost,
    L: LoginsHost,
> AccountsServiceImpl<H, P, D, S, F, W, C, L>
{
    /// Creates the service around `operations`, the four hosts its deletions
    /// need and the three hosts its suspension state reads.
    ///
    /// Eight parameters, and they stay eight rather than being bundled into one
    /// or two structs. Each is a distinct seam onto the machine, every one is a
    /// different trait, and the compiler therefore refuses a swapped pair — the
    /// hazard a long parameter list normally carries. A bundle would move the
    /// same eight names one level down and add a type whose only purpose is to
    /// be constructed at the one call site that builds this service
    /// (`server.rs`), while making a host left out of the bundle a `Default`
    /// away from silently doing nothing. The same reasoning is written out on
    /// `ops::backup::restore_backup`, which carries this allow for the same
    /// reason.
    #[allow(clippy::too_many_arguments)]
    #[must_use]
    pub fn new(
        operations: AccountOperations<H>,
        php_host: P,
        db_host: D,
        sftp_host: S,
        ftps_host: F,
        site_host: W,
        cron_host: C,
        logins_host: L,
    ) -> Self {
        Self {
            operations: Arc::new(operations),
            php_host: Arc::new(php_host),
            db_host: Arc::new(db_host),
            sftp_host: Arc::new(sftp_host),
            ftps_host: Arc::new(ftps_host),
            site_host: Arc::new(site_host),
            cron_host: Arc::new(cron_host),
            logins_host: Arc::new(logins_host),
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
impl<
    H: SystemHost + 'static,
    P: PhpHost + 'static,
    D: DbHost + 'static,
    S: SftpHost + 'static,
    F: FtpsHost + 'static,
    W: SiteHost + 'static,
    C: CronHost + 'static,
    L: LoginsHost + 'static,
> AccountsService for AccountsServiceImpl<H, P, D, S, F, W, C, L>
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
                // All four hosts travel in here so the deletion can take the
                // account's databases, its SFTP logins and jail, its FTPS
                // logins and jail, and its php-fpm pools with it, BEFORE
                // `userdel` removes the user
                // those things name; see `AccountOperations::delete` for why
                // that order is the only safe one, and why leaving any of them
                // behind is the defect that cannot be repaired afterwards.
                // They are cloned rather than borrowed because the closure
                // outlives this call: it runs on the blocking pool.
                let operations = Arc::clone(&self.operations);
                let php_host = Arc::clone(&self.php_host);
                let db_host = Arc::clone(&self.db_host);
                let sftp_host = Arc::clone(&self.sftp_host);
                let ftps_host = Arc::clone(&self.ftps_host);
                move |name| {
                    operations.delete(
                        php_host.as_ref(),
                        db_host.as_ref(),
                        sftp_host.as_ref(),
                        ftps_host.as_ref(),
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

    /// Reports what this host can be observed to be doing for the account.
    async fn get_account_suspension_state(
        &self,
        request: Request<GetAccountSuspensionStateRequest>,
    ) -> Result<Response<GetAccountSuspensionStateResponse>, Status> {
        let request = request.into_inner();
        let result = match self
            .with_account(&request.username, {
                let operations = Arc::clone(&self.operations);
                let site_host = Arc::clone(&self.site_host);
                let cron_host = Arc::clone(&self.cron_host);
                let logins_host = Arc::clone(&self.logins_host);
                move |name| {
                    operations.suspension_state(
                        site_host.as_ref(),
                        cron_host.as_ref(),
                        logins_host.as_ref(),
                        &name,
                    )
                }
            })
            .await
        {
            Ok(state) => {
                get_account_suspension_state_response::Result::Ok(GetAccountSuspensionStateOk {
                    login_locked: state.login_locked,
                    login_password_state: to_login_password_state(state.login_password) as i32,
                    sites_directory_readable: state.sites_directory_readable,
                    sites: state
                        .sites
                        .into_iter()
                        .map(|fact| SiteSuspensionFact {
                            domain: fact.domain,
                            serving_stub: fact.serving_stub,
                        })
                        .collect(),
                    cron_entries_total: state.cron.entries_total,
                    cron_entries_suspended: state.cron.entries_suspended,
                    cron_foreign_lines: state.cron.foreign_lines,
                    // Every login the account holds, of BOTH protocols, in the
                    // one repeated field the contract has today. Reporting only
                    // the SFTP half would hide from the panel exactly the
                    // credential `ops::logins` exists to make visible; the
                    // protocol beside each name, and the count of entries this
                    // agent does not manage, reach the wire with the field that
                    // carries them.
                    sftp_logins: state
                        .logins
                        .logins
                        .into_iter()
                        .map(|login| SftpLoginSuspensionFact {
                            username: login.name,
                            locked: login.locked,
                            protocol: to_transfer_protocol(login.protocol) as i32,
                        })
                        .collect(),
                    // The number the enumeration deliberately cannot speak for:
                    // passwd entries on this account's uid whose home is NEITHER
                    // jail. The agent locks none of them — they are not its
                    // entries — so the attestation REPORTS the count instead of
                    // refusing on it, which is the shape `cron_foreign_lines`
                    // set for the same question about a crontab. A suspension
                    // that said nothing about them would be claiming a silence
                    // it never achieved.
                    unmanaged_logins: state.logins.unmanaged,
                })
            }
            Err(error) => get_account_suspension_state_response::Result::Error(error),
        };

        Ok(Response::new(GetAccountSuspensionStateResponse {
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

    /// Re-groups every hosting account's home directory to the web server's
    /// group where it is not already that group.
    ///
    /// The one rpc in this file that takes no account name, for the same
    /// reason `repair_database_grants` in `services::db` takes none: the set
    /// of homes it repairs belongs to the host rather than to a tenant, and
    /// `accounts.proto` says why a per-account pass would be the wrong shape.
    /// There is nothing to re-validate: the request carries a single boolean,
    /// and every account name and path the operation acts on is read from the
    /// password database and decoded by [`maran_agent_core::validation::system::name::AccountName`]
    /// itself.
    async fn repair_account_home_groups(
        &self,
        request: Request<RepairAccountHomeGroupsRequest>,
    ) -> Result<Response<RepairAccountHomeGroupsResponse>, Status> {
        let report_only = request.into_inner().report_only;
        let operations = Arc::clone(&self.operations);

        let result = run_blocking(ACCOUNT_OPERATION, to_agent_error, move || {
            operations.repair_home_groups(report_only)
        })
        .await;

        let result = match result {
            Ok(report) => repair_account_home_groups_response::Result::Ok(
                wire_home_group_repair_report(report),
            ),
            Err(error) => repair_account_home_groups_response::Result::Error(error),
        };

        Ok(Response::new(RepairAccountHomeGroupsResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/accounts_service_tests.rs"]
mod tests;
