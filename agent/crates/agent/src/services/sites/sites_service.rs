//! `SitesService`: the vhosts the agent owns, and the document roots they serve.

use std::pin::Pin;
use std::sync::Arc;

use maran_agent_core::validation::php_version::PhpVersion;
use maran_ops::php::PhpHost;
use maran_ops::sites::{
    self, CreateSiteInput, PhpSwitch, SiteHost, SiteIdentity, SiteLogKind, SiteMaintenanceHost,
    SitesOpError,
};
use tokio::sync::mpsc;
use tokio_stream::Stream;
use tokio_stream::wrappers::ReceiverStream;
use tonic::{Request, Response, Status};

use crate::proto::sites_service_server::SitesService;
use crate::proto::{
    AgentError, CreateSiteOk, CreateSiteRequest, CreateSiteResponse, DeleteSiteOk,
    DeleteSiteRequest, DeleteSiteResponse, DisableSiteOk, DisableSiteRequest, DisableSiteResponse,
    EnableSiteOk, EnableSiteRequest, EnableSiteResponse, ErrorCode, ReloadWebServerOk,
    ReloadWebServerRequest, ReloadWebServerResponse, SiteLogKind as ProtoSiteLogKind, SiteSpec,
    TailSiteLogRequest, TailSiteLogResponse, UpdateSitePhpVersionOk, UpdateSitePhpVersionRequest,
    UpdateSitePhpVersionResponse, create_site_response, delete_site_response,
    disable_site_response, enable_site_response, reload_web_server_response,
    tail_site_log_response, update_site_php_version_response,
};
use crate::services::sites::invalid_input::invalid_input;
use crate::services::sites::site_status::to_agent_error;
use crate::services::sites::stream_log_sink::StreamLogSink;
use crate::services::sites::tail_terminal::tail_terminal;
use crate::services::sites::validated_identity::validated_identity;
use crate::services::sites::validated_overrides::validated_overrides;
use crate::services::sites::validated_site::validated_site;

/// How many log lines the tail may run ahead of the client.
///
/// The ceiling on how far the tail may run ahead of the client, and therefore
/// on what one open stream costs the root daemon. Sixty-four lines is a
/// fraction of a second of a busy access log and one small allocation.
///
/// A full channel is not by itself the whole of the backpressure any more:
/// `StreamLogSink` retries a full channel for a bounded time and then ends the
/// tail as `TailEnd::ClientStalled`, so a client that stops reading is
/// eventually dropped rather than either parking a blocking thread forever or
/// growing a queue here. The capacity decides how much slack a merely slow
/// client gets before that clock starts.
const LOG_CHANNEL_CAPACITY: usize = 64;

/// The stream `TailSiteLog` returns.
type TailStream = Pin<Box<dyn Stream<Item = Result<TailSiteLogResponse, Status>> + Send>>;

/// Serves the site operations over the wire.
///
/// Every rpc follows the same three steps: revalidate what the panel sent, run
/// one operation, and map the outcome into the response's `oneof`. Failures
/// travel in the payload rather than as a gRPC status, because they are
/// answers the panel acts on — a site that already exists is information, not
/// a transport error (rules/proto.md).
///
/// Two hosts, because one rpc spans two areas: switching a site's PHP version
/// rewrites the vhost AND the account's php-fpm pool, and the pool is the PHP
/// area's to write. They are held as `Arc`s so a blocking task can own a
/// handle: every operation here forks or spawns a process, which
/// `rules/rust.md` and `SiteHost` both require to happen off the runtime's
/// workers — `fork` then blocks in `waitpid`, and on a worker thread that
/// stalls every other in-flight command.
pub struct SitesServiceImpl<S, P> {
    /// The machine the site operations run against.
    host: Arc<S>,
    /// The machine the pool write runs against.
    php_host: Arc<P>,
    /// Where platform facts come from. A service never branches on a
    /// distribution itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<S, P> SitesServiceImpl<S, P>
where
    S: SiteHost + SiteMaintenanceHost + 'static,
    P: PhpHost + 'static,
{
    /// Creates the service around the hosts it runs operations against.
    #[must_use]
    pub fn new(host: S, php_host: P, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            php_host: Arc::new(php_host),
            distro,
        }
    }

    /// Runs one operation on the blocking pool and maps its failure onto the
    /// wire error — the shape every unary rpc here shares, written once so
    /// that adding an rpc cannot forget to leave the runtime or map an error
    /// differently from its neighbours.
    ///
    /// # Errors
    ///
    /// Returns the [`to_agent_error`] mapping of whatever the operation failed
    /// on. A blocking task that panicked has no domain answer to give and is
    /// reported as a system failure — not as a gRPC status, which rules/proto.md
    /// reserves for transport problems, and a panic inside the agent is not one.
    async fn run<T, F>(operation: F) -> Result<T, AgentError>
    where
        F: FnOnce() -> Result<T, SitesOpError> + Send + 'static,
        T: Send + 'static,
    {
        match tokio::task::spawn_blocking(operation).await {
            Ok(outcome) => outcome.map_err(|error| to_agent_error(&error)),
            Err(error) => Err(AgentError {
                code: ErrorCode::SystemFailure as i32,
                message: format!("the site operation did not finish: {error}"),
                tool_output: String::new(),
            }),
        }
    }

    /// The site description a re-rendering rpc carries, validated.
    ///
    /// # Errors
    ///
    /// Returns the wire error for anything the agent will not accept.
    fn site(
        account_username: &str,
        domain: &str,
        spec: Option<&SiteSpec>,
    ) -> Result<CreateSiteInput, AgentError> {
        validated_site(account_username, domain, spec)
    }
}

#[tonic::async_trait]
impl<S, P> SitesService for SitesServiceImpl<S, P>
where
    S: SiteHost + SiteMaintenanceHost + 'static,
    P: PhpHost + 'static,
{
    /// The stream `TailSiteLog` returns.
    type TailSiteLogStream = TailStream;

    /// Creates the document root, renders the vhost, validates it and reloads.
    async fn create_site(
        &self,
        request: Request<CreateSiteRequest>,
    ) -> Result<Response<CreateSiteResponse>, Status> {
        let request = request.into_inner();
        // CreateSite carries the site's description in its own fields, since
        // it is the rpc that establishes them; every other rpc restates them
        // in a `SiteSpec`. Both arrive at the same validated input.
        let spec = SiteSpec {
            aliases: request.aliases,
            backend_type: request.backend_type,
            php_version: request.php_version,
            proxy_upstream: request.proxy_upstream,
            // A site is created before it has a certificate: SSL is installed
            // afterwards, by `SslService`, which re-renders this same vhost.
            has_certificate: false,
        };

        let validated = Self::site(&request.account_username, &request.domain, Some(&spec))
            .and_then(|input| {
                // Revalidated here rather than trusted, exactly as the switch
                // does: these two fields reach a pool file, which is
                // line-oriented, and the panel having sent them is not why they
                // are safe (rules/security.md item 1).
                let overrides = validated_overrides(&request.overrides)?;
                Ok((input, overrides))
            });

        let result = match validated {
            Ok((input, overrides)) => {
                let (host, php_host, distro) = (
                    Arc::clone(&self.host),
                    Arc::clone(&self.php_host),
                    self.distro,
                );
                let max_children = request.max_children;
                Self::run(move || {
                    sites::create_site(
                        host.as_ref(),
                        php_host.as_ref(),
                        distro,
                        &input,
                        max_children,
                        &overrides,
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(created) => create_site_response::Result::Ok(CreateSiteOk {
                document_root: created.document_root,
            }),
            Err(error) => create_site_response::Result::Error(error),
        };

        Ok(Response::new(CreateSiteResponse {
            result: Some(result),
        }))
    }

    /// Switches a PHP site to another installed version, rewriting its pool.
    async fn update_site_php_version(
        &self,
        request: Request<UpdateSitePhpVersionRequest>,
    ) -> Result<Response<UpdateSitePhpVersionResponse>, Status> {
        let request = request.into_inner();

        let validated = Self::site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        )
        .and_then(|input| {
            let version =
                maran_agent_core::validation::php_version::PhpVersion::parse(&request.php_version)
                    .map_err(|error| AgentError {
                        code: ErrorCode::InvalidInput as i32,
                        message: error.to_string(),
                        tool_output: String::new(),
                    })?;
            let overrides = validated_overrides(&request.overrides)?;
            Ok((input, version, overrides))
        });

        let result = match validated {
            Ok((input, version, overrides)) => {
                let (host, php_host, distro) = (
                    Arc::clone(&self.host),
                    Arc::clone(&self.php_host),
                    self.distro,
                );
                let max_children = request.max_children;
                let remove_previous_pool = request.remove_previous_pool;
                Self::run(move || {
                    sites::update_site_php_version(
                        host.as_ref(),
                        php_host.as_ref(),
                        distro,
                        &input,
                        &PhpSwitch {
                            version: &version,
                            max_children,
                            overrides: &overrides,
                            remove_previous_pool,
                        },
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => update_site_php_version_response::Result::Ok(UpdateSitePhpVersionOk {}),
            Err(error) => update_site_php_version_response::Result::Error(error),
        };

        Ok(Response::new(UpdateSitePhpVersionResponse {
            result: Some(result),
        }))
    }

    /// Restores the site's own vhost.
    async fn enable_site(
        &self,
        request: Request<EnableSiteRequest>,
    ) -> Result<Response<EnableSiteResponse>, Status> {
        let request = request.into_inner();
        let result = match Self::site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        ) {
            Ok(input) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                Self::run(move || sites::enable_site(host.as_ref(), distro, &input)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => enable_site_response::Result::Ok(EnableSiteOk {}),
            Err(error) => enable_site_response::Result::Error(error),
        };

        Ok(Response::new(EnableSiteResponse {
            result: Some(result),
        }))
    }

    /// Replaces the site's vhost with the suspension one, keeping the domain.
    async fn disable_site(
        &self,
        request: Request<DisableSiteRequest>,
    ) -> Result<Response<DisableSiteResponse>, Status> {
        let request = request.into_inner();
        let result = match Self::site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        ) {
            Ok(input) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                Self::run(move || sites::disable_site(host.as_ref(), distro, &input)).await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => disable_site_response::Result::Ok(DisableSiteOk {}),
            Err(error) => disable_site_response::Result::Error(error),
        };

        Ok(Response::new(DisableSiteResponse {
            result: Some(result),
        }))
    }

    /// Removes the vhost and reloads. The document root is left alone.
    async fn delete_site(
        &self,
        request: Request<DeleteSiteRequest>,
    ) -> Result<Response<DeleteSiteResponse>, Status> {
        let request = request.into_inner();

        // The one rpc that needs no `SiteSpec`: deleting touches only the
        // vhost in the agent's own include directory, which is named from the
        // account and the domain alone. Nothing is re-rendered, so there is
        // nothing to be faithful to — and requiring the description would make
        // a site whose PHP version has since been removed impossible to clean
        // up. `SiteIdentity` is the operation's input for exactly that reason:
        // a service with only these two facts must not be able to assert the
        // others.
        let result = match validated_identity(&request.account_username, &request.domain).and_then(
            |(account, domain)| {
                // Revalidated like every other input, and ABSENT is a MEANING
                // rather than a default: the empty string is the panel saying
                // "leave the pool alone", which is what it says for a static
                // site and for a PHP site whose account still has other sites
                // on the same version. A version that is present but malformed
                // is refused rather than read as absent — the two must never
                // collapse into each other, because one leaves a pool standing
                // and the other takes one away.
                let retired = if request.retired_php_version.is_empty() {
                    None
                } else {
                    Some(
                        PhpVersion::parse(&request.retired_php_version)
                            .map_err(|error| invalid_input(error.to_string()))?,
                    )
                };
                Ok((account, domain, retired))
            },
        ) {
            Ok((account, domain, retired)) => {
                let (host, php_host, distro) = (
                    Arc::clone(&self.host),
                    Arc::clone(&self.php_host),
                    self.distro,
                );
                let site = SiteIdentity { account, domain };
                Self::run(move || {
                    sites::delete_site(
                        host.as_ref(),
                        php_host.as_ref(),
                        distro,
                        &site,
                        retired.as_ref(),
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => delete_site_response::Result::Ok(DeleteSiteOk {}),
            Err(error) => delete_site_response::Result::Error(error),
        };

        Ok(Response::new(DeleteSiteResponse {
            result: Some(result),
        }))
    }

    /// Streams the site's log: the historical tail first, then what is
    /// appended.
    ///
    /// The stream is produced by `ops` and this only wraps it
    /// (rules/rust.md). It is bounded at both ends: the history is capped by
    /// the operation at 1000 lines as `sites.proto` states, and the follow
    /// runs on a blocking thread whose every send goes into a bounded channel
    /// — so a client that stops reading applies backpressure, and a client
    /// that drops the stream closes the channel, which the operation's
    /// callback reports as "stop" on the very next line.
    async fn tail_site_log(
        &self,
        request: Request<TailSiteLogRequest>,
    ) -> Result<Response<Self::TailSiteLogStream>, Status> {
        let request = request.into_inner();
        let (sender, receiver) = mpsc::channel(LOG_CHANNEL_CAPACITY);

        let kind = match ProtoSiteLogKind::try_from(request.kind) {
            Ok(ProtoSiteLogKind::Access) => Some(SiteLogKind::Access),
            Ok(ProtoSiteLogKind::Error) => Some(SiteLogKind::Error),
            Ok(ProtoSiteLogKind::Unspecified) | Err(_) => None,
        };

        let identity = validated_identity(&request.account_username, &request.domain);

        match (identity, kind) {
            (Ok((account, domain)), Some(kind)) => {
                let host = Arc::clone(&self.host);
                let history = request.history_lines;
                let mut sink = StreamLogSink::new(sender.clone());

                tokio::task::spawn_blocking(move || {
                    let outcome = sites::tail_site_log(
                        host.as_ref(),
                        &account,
                        &domain,
                        kind,
                        history,
                        &mut sink,
                    );

                    // A domain outcome in the payload, not a status
                    // (rules/proto.md). Every ending that is not the client's
                    // own choice sends one of these, so a stream never just
                    // stops without saying why.
                    if let Some(error) = tail_terminal(&outcome) {
                        // Best effort by construction: a closed channel means
                        // the client is already gone, and a stalled one may
                        // still drain in time to see why it was dropped.
                        let _ = sender.blocking_send(Ok(TailSiteLogResponse {
                            result: Some(tail_site_log_response::Result::Error(error)),
                        }));
                    }
                });
            }
            (identity, _) => {
                // Either the identity was refused, or the log kind was — in
                // which case the identity was fine and the kind is what is
                // reported.
                let error = match identity {
                    Err(error) => error,
                    Ok(_) => AgentError {
                        code: ErrorCode::InvalidInput as i32,
                        message: format!("unknown site log kind {}", request.kind),
                        tool_output: String::new(),
                    },
                };

                // One message and then the end of the stream: the caller gets
                // the same typed refusal a unary rpc would have given it.
                let _ = sender
                    .send(Ok(TailSiteLogResponse {
                        result: Some(tail_site_log_response::Result::Error(error)),
                    }))
                    .await;
            }
        }

        Ok(Response::new(Box::pin(ReceiverStream::new(receiver))))
    }

    /// Validates the configuration on disk and reloads the web server.
    async fn reload_web_server(
        &self,
        _request: Request<ReloadWebServerRequest>,
    ) -> Result<Response<ReloadWebServerResponse>, Status> {
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let result = match Self::run(move || sites::reload_web_server(host.as_ref(), distro)).await
        {
            Ok(()) => reload_web_server_response::Result::Ok(ReloadWebServerOk {}),
            Err(error) => reload_web_server_response::Result::Error(error),
        };

        Ok(Response::new(ReloadWebServerResponse {
            result: Some(result),
        }))
    }
}
