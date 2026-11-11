//! `MailService`: the host's mail stack, and the data it serves.

use std::sync::Arc;

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::mail::{self, MailHost};
use tonic::{Request, Response, Status};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::mail_service_server::MailService;
use crate::proto::{
    AgentError, ConfigureMailDomainRequest, ConfigureMailDomainResponse,
    DeleteQueuedMessageRequest, DeleteQueuedMessageResponse, DisableMailOk, DisableMailRequest,
    DisableMailResponse, EnableMailOk, EnableMailRequest, EnableMailResponse,
    FlushMailQueueRequest, FlushMailQueueResponse, GetMailStatusOk, GetMailStatusRequest,
    GetMailStatusResponse, ListMailQueueRequest, ListMailQueueResponse, ListMailboxUsageRequest,
    ListMailboxUsageResponse, ReloadMailTlsOk, ReloadMailTlsRequest, ReloadMailTlsResponse,
    RemoveAccountMailRequest, RemoveAccountMailResponse, RemoveLocalSubmissionOk,
    RemoveLocalSubmissionRequest, RemoveLocalSubmissionResponse, RemoveMailDomainRequest,
    RemoveMailDomainResponse, RemoveMailboxRequest, RemoveMailboxResponse,
    SetAccountMailSuspensionRequest, SetAccountMailSuspensionResponse, SetLocalSubmissionOk,
    SetLocalSubmissionRequest, SetLocalSubmissionResponse, SetMailAliasesRequest,
    SetMailAliasesResponse, SetMailboxAutoresponderRequest, SetMailboxAutoresponderResponse,
    SetMailboxRequest, SetMailboxResponse, configure_mail_domain_response,
    delete_queued_message_response, disable_mail_response, enable_mail_response,
    flush_mail_queue_response, get_mail_status_response, list_mail_queue_response,
    list_mailbox_usage_response, reload_mail_tls_response, remove_account_mail_response,
    remove_local_submission_response, remove_mail_domain_response, remove_mailbox_response,
    set_account_mail_suspension_response, set_local_submission_response, set_mail_aliases_response,
    set_mailbox_autoresponder_response, set_mailbox_response,
};
use crate::services::mail::mail_status::to_agent_error;
use crate::services::mail::refuse_data_plane::refuse_data_plane;
use crate::services::mail::reported_mail_status::reported_mail_status;
use crate::services::mail::resolved_uid::resolved_uid;
use crate::services::mail::validated_local_submission::validated_local_submission;
use crate::services::mail::validated_mail_settings::validated_mail_settings;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::run_blocking::run_blocking;

/// Serves the mail stack over the wire.
///
/// The four host-lifecycle rpcs (`GetMailStatus`, `EnableMail`, `DisableMail`,
/// `ReloadMailTls`) follow the same three steps as every other service: take the
/// request, run one operation, map the outcome into the response's `oneof`.
/// Failures travel in the payload rather than as a gRPC status, because they are
/// answers the panel acts on (rules/proto.md). The twelve data-plane rpcs
/// (domains, mailboxes, aliases, autoresponders, suspension, removal, usage and
/// the queue) answer a typed `NOT_IMPLEMENTED` until their operations land (the two local
/// submission rpcs, `SetLocalSubmission` and `RemoveLocalSubmission`, are carried); the
/// tonic trait is complete so the whole contract is served from the first
/// release that carries it.
///
/// No rpc takes a path, a program, an argument or a template. The only request
/// values that reach a configuration file are the validated hostname, message
/// size and spam scores of `EnableMail`; the certificate is the panel's own,
/// found by the agent in its fixed store.
pub struct MailServiceImpl<H> {
    /// The machine the mail operations run against.
    host: Arc<H>,
    /// Where platform facts come from: the daemons' programs and units, the
    /// service manager, the password scheme. A service never branches on a
    /// distribution itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
    /// The os-release `VERSION_ID` read at start. The Dovecot configuration
    /// generation and the key-value server are facts of the release, not of the
    /// family.
    release: Arc<str>,
}

impl<H: MailHost + 'static> MailServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(
        host: H,
        distro: &'static dyn maran_distro::DistroAdapter,
        release: impl Into<Arc<str>>,
    ) -> Self {
        Self {
            host: Arc::new(host),
            distro,
            release: release.into(),
        }
    }

    /// The password scheme this host's distribution says its IMAP server verifies.
    fn host_scheme(&self) -> MailPasswordScheme {
        self.distro.mail_password_scheme()
    }
}

#[tonic::async_trait]
impl<H: MailHost + 'static> MailService for MailServiceImpl<H> {
    /// Reports the stack as the host shows it now. Read-only: it changes nothing
    /// and leaves no audit line.
    async fn get_mail_status(
        &self,
        _request: Request<GetMailStatusRequest>,
    ) -> Result<Response<GetMailStatusResponse>, Status> {
        let host = Arc::clone(&self.host);
        let release = Arc::clone(&self.release);
        let distro = self.distro;
        let result = run_blocking("mail status reading", to_agent_error, move || {
            mail::read_mail_status(host.as_ref(), distro, &release)
        })
        .await;

        let result = match result {
            Ok(observed) => get_mail_status_response::Result::Ok(GetMailStatusOk {
                status: Some(reported_mail_status(&observed, self.host_scheme())),
            }),
            Err(error) => get_mail_status_response::Result::Error(error),
        };

        Ok(Response::new(GetMailStatusResponse {
            result: Some(result),
        }))
    }

    /// Renders, checks and applies the host's mail configuration, then starts the
    /// daemons in dependency order and reports what they do.
    ///
    /// Cancellation class: run to completion. A half-written configuration of
    /// four daemons is the one state nothing recovers from by itself, and the work
    /// is inside `spawn_blocking`, which a dropped client cannot stop; the call is
    /// idempotent, so the panel's remedy for a response it lost is to ask again.
    async fn enable_mail(
        &self,
        request: Request<EnableMailRequest>,
    ) -> Result<Response<EnableMailResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let result = match validated_mail_settings(&request) {
            Ok(settings) => {
                let host = Arc::clone(&self.host);
                let release = Arc::clone(&self.release);
                let distro = self.distro;
                run_blocking("mail operation", to_agent_error, move || {
                    mail::enable_mail(host.as_ref(), distro, &release, &settings)
                        .map(|state| mail::observe_mail(host.as_ref(), distro, state))
                })
                .await
            }
            Err(error) => Err(error),
        };

        audit.settle(&result);
        let result = match result {
            Ok(observed) => enable_mail_response::Result::Ok(EnableMailOk {
                status: Some(reported_mail_status(&observed, self.host_scheme())),
            }),
            Err(error) => enable_mail_response::Result::Error(error),
        };

        Ok(Response::new(EnableMailResponse {
            result: Some(result),
        }))
    }

    /// Stops the daemons, takes them out of the boot sequence and masks their
    /// units, leaving every configuration file and every mailbox where it is.
    ///
    /// Cancellation class: run to completion, for the reason `enable_mail` gives;
    /// a repeated disable converges.
    async fn disable_mail(
        &self,
        request: Request<DisableMailRequest>,
    ) -> Result<Response<DisableMailResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let host = Arc::clone(&self.host);
        let release = Arc::clone(&self.release);
        let distro = self.distro;
        let result = run_blocking("mail operation", to_agent_error, move || {
            mail::disable_mail(host.as_ref(), distro, &release)
                .map(|state| mail::observe_mail(host.as_ref(), distro, state))
        })
        .await;

        audit.settle(&result);
        let result = match result {
            Ok(observed) => disable_mail_response::Result::Ok(DisableMailOk {
                status: Some(reported_mail_status(&observed, self.host_scheme())),
            }),
            Err(error) => disable_mail_response::Result::Error(error),
        };

        Ok(Response::new(DisableMailResponse {
            result: Some(result),
        }))
    }

    /// Makes the running daemons that hold the certificate pick up the panel's
    /// current one, without restarting them.
    ///
    /// Cancellation class: run to completion, for the reason `enable_mail` gives;
    /// a repeated reload converges.
    async fn reload_mail_tls(
        &self,
        request: Request<ReloadMailTlsRequest>,
    ) -> Result<Response<ReloadMailTlsResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let host = Arc::clone(&self.host);
        let release = Arc::clone(&self.release);
        let distro = self.distro;
        let result = run_blocking("mail operation", to_agent_error, move || {
            mail::reload_mail_tls(host.as_ref(), distro, &release)
                .map(|state| mail::observe_mail(host.as_ref(), distro, state))
        })
        .await;

        audit.settle(&result);
        let result = match result {
            Ok(observed) => reload_mail_tls_response::Result::Ok(ReloadMailTlsOk {
                status: Some(reported_mail_status(&observed, self.host_scheme())),
            }),
            Err(error) => reload_mail_tls_response::Result::Error(error),
        };

        Ok(Response::new(ReloadMailTlsResponse {
            result: Some(result),
        }))
    }

    /// Makes one domain deliverable and signable for an account.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn configure_mail_domain(
        &self,
        request: Request<ConfigureMailDomainRequest>,
    ) -> Result<Response<ConfigureMailDomainResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(ConfigureMailDomainResponse {
            result: Some(configure_mail_domain_response::Result::Error(error)),
        }))
    }

    /// Removes one domain and its signing key.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn remove_mail_domain(
        &self,
        request: Request<RemoveMailDomainRequest>,
    ) -> Result<Response<RemoveMailDomainResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(RemoveMailDomainResponse {
            result: Some(remove_mail_domain_response::Result::Error(error)),
        }))
    }

    /// Creates or updates one mailbox.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn set_mailbox(
        &self,
        request: Request<SetMailboxRequest>,
    ) -> Result<Response<SetMailboxResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(SetMailboxResponse {
            result: Some(set_mailbox_response::Result::Error(error)),
        }))
    }

    /// Removes one mailbox.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn remove_mailbox(
        &self,
        request: Request<RemoveMailboxRequest>,
    ) -> Result<Response<RemoveMailboxResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(RemoveMailboxResponse {
            result: Some(remove_mailbox_response::Result::Error(error)),
        }))
    }

    /// Replaces every alias, forwarder and catch-all of one domain.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn set_mail_aliases(
        &self,
        request: Request<SetMailAliasesRequest>,
    ) -> Result<Response<SetMailAliasesResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(SetMailAliasesResponse {
            result: Some(set_mail_aliases_response::Result::Error(error)),
        }))
    }

    /// Installs, replaces or clears one mailbox's out-of-office reply.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn set_mailbox_autoresponder(
        &self,
        request: Request<SetMailboxAutoresponderRequest>,
    ) -> Result<Response<SetMailboxAutoresponderResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(SetMailboxAutoresponderResponse {
            result: Some(set_mailbox_autoresponder_response::Result::Error(error)),
        }))
    }

    /// Locks or unlocks every mailbox of an account.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn set_account_mail_suspension(
        &self,
        request: Request<SetAccountMailSuspensionRequest>,
    ) -> Result<Response<SetAccountMailSuspensionResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(SetAccountMailSuspensionResponse {
            result: Some(set_account_mail_suspension_response::Result::Error(error)),
        }))
    }

    /// Removes everything mail-related of an account.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn remove_account_mail(
        &self,
        request: Request<RemoveAccountMailRequest>,
    ) -> Result<Response<RemoveAccountMailResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(RemoveAccountMailResponse {
            result: Some(remove_account_mail_response::Result::Error(error)),
        }))
    }

    /// Reads the usage of every mailbox of an account.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn list_mailbox_usage(
        &self,
        request: Request<ListMailboxUsageRequest>,
    ) -> Result<Response<ListMailboxUsageResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(ListMailboxUsageResponse {
            result: Some(list_mailbox_usage_response::Result::Error(error)),
        }))
    }

    /// Lists the MTA's queue.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn list_mail_queue(
        &self,
        request: Request<ListMailQueueRequest>,
    ) -> Result<Response<ListMailQueueResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(ListMailQueueResponse {
            result: Some(list_mail_queue_response::Result::Error(error)),
        }))
    }

    /// Deletes one queued message.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn delete_queued_message(
        &self,
        request: Request<DeleteQueuedMessageRequest>,
    ) -> Result<Response<DeleteQueuedMessageResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(DeleteQueuedMessageResponse {
            result: Some(delete_queued_message_response::Result::Error(error)),
        }))
    }

    /// Asks the MTA to retry every deferred message.
    ///
    /// Not carried by this build yet: answers `NOT_IMPLEMENTED`.
    async fn flush_mail_queue(
        &self,
        request: Request<FlushMailQueueRequest>,
    ) -> Result<Response<FlushMailQueueResponse>, Status> {
        let error = refuse_data_plane(request.extensions());
        Ok(Response::new(FlushMailQueueResponse {
            result: Some(flush_mail_queue_response::Result::Error(error)),
        }))
    }

    /// Binds an account's locally submitted mail to its own sender domains and
    /// send limit: renders the filter's maps, then reloads the filter.
    ///
    /// Cancellation class: run to completion. The maps are written with the
    /// safe-write protocol inside `spawn_blocking`, which a dropped client cannot
    /// stop; the call replaces the account's whole binding, so a repeat converges.
    async fn set_local_submission(
        &self,
        request: Request<SetLocalSubmissionRequest>,
    ) -> Result<Response<SetLocalSubmissionResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let host = Arc::clone(&self.host);
        let distro = self.distro;
        let result = run_blocking("mail operation", AgentError::clone, move || {
            let submission = validated_local_submission(&request, resolved_uid)?;
            mail::set_local_submission(host.as_ref(), distro, &submission)
                .map_err(|error| to_agent_error(&error))
        })
        .await;

        audit.settle(&result);
        let result = match result {
            Ok(()) => set_local_submission_response::Result::Ok(SetLocalSubmissionOk {}),
            Err(error) => set_local_submission_response::Result::Error(error),
        };

        Ok(Response::new(SetLocalSubmissionResponse {
            result: Some(result),
        }))
    }

    /// Removes an account's local submission binding, so its system user can no
    /// longer submit mail locally.
    ///
    /// Cancellation class: run to completion, for the reason `set_local_submission`
    /// gives; removing a binding that is gone is a success.
    async fn remove_local_submission(
        &self,
        request: Request<RemoveLocalSubmissionRequest>,
    ) -> Result<Response<RemoveLocalSubmissionResponse>, Status> {
        let audit = AuditSlot::of(request.extensions());
        let request = request.into_inner();

        let result = match AccountName::parse(&request.account_username) {
            Ok(account) => {
                let host = Arc::clone(&self.host);
                let distro = self.distro;
                run_blocking("mail operation", to_agent_error, move || {
                    mail::remove_local_submission(host.as_ref(), distro, account.as_str())
                })
                .await
            }
            Err(error) => Err(invalid_input(format!("account: {error}"))),
        };

        audit.settle(&result);
        let result = match result {
            Ok(()) => remove_local_submission_response::Result::Ok(RemoveLocalSubmissionOk {}),
            Err(error) => remove_local_submission_response::Result::Error(error),
        };

        Ok(Response::new(RemoveLocalSubmissionResponse {
            result: Some(result),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/mail/mail_service_tests.rs"]
mod tests;
