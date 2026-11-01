//! `SslService`: certificate material on disk, and the vhost that points at it.

use std::convert::Infallible;
use std::sync::Arc;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_ops::ssl::{self, CertificateMaterial, PanelChallengeHost, SslHost};
use tonic::{Request, Response, Status};

use crate::proto::ssl_service_server::SslService;
use crate::proto::{
    GenerateSelfSignedOk, GenerateSelfSignedRequest, GenerateSelfSignedResponse,
    InstallCertificateRequest, InstallCertificateResponse, InstallPanelCertificateRequest,
    InstallPanelCertificateResponse, RemoveCertificateOk, RemoveCertificateRequest,
    RemoveCertificateResponse, RemovePanelChallengeOk, RemovePanelChallengeRequest,
    RemovePanelChallengeResponse, WritePanelChallengeOk, WritePanelChallengeRequest,
    WritePanelChallengeResponse, generate_self_signed_response, install_certificate_response,
    install_panel_certificate_response, remove_certificate_response,
    remove_panel_challenge_response, write_panel_challenge_response,
};
use crate::services::sites::validated_existing_site::validated_existing_site;
use crate::services::sites::validated_site::validated_site;
use crate::services::ssl::install_outcome::install_outcome;
use crate::services::ssl::panel_install_outcome::panel_install_outcome;
use crate::services::ssl::ssl_status::to_agent_error;
use crate::services::ssl::validated_panel_challenge::ValidatedPanelChallenge;
use crate::services::ssl::validated_panel_token::validated_panel_token;
use crate::services::wire::run_blocking::run_blocking;

/// Serves the certificate operations over the wire.
///
/// Every rpc follows the same three steps: revalidate what the panel sent, run
/// one operation, and map the outcome into the response's `oneof`. Failures
/// travel in the payload rather than as a gRPC status, because they are
/// answers the panel acts on — a certificate that is already there is
/// information, not a transport error (rules/proto.md).
///
/// The site description is validated by the SITE area's
/// [`validated_site`], not by a second copy of that code here: an installation
/// re-renders the site's vhost, and it must render the same text `create_site`
/// rendered. Two validators would be two opinions about what the site is, and
/// they would differ on the vhost a browser reaches.
pub struct SslServiceImpl<H, C> {
    /// The machine the certificate operations run against.
    host: Arc<H>,
    /// Where the panel's own HTTP-01 challenge files are written (issue #132).
    challenges: Arc<C>,
    /// The free-space floor a panel challenge write is refused below, as a
    /// customer's challenge through `WriteFile` is (issue #148).
    floor: FreeSpaceFloor,
    /// Where platform facts come from — the openssl binary, the nginx binary
    /// and the service manager. A service never branches on a distribution
    /// itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: SslHost + 'static, C: PanelChallengeHost + 'static> SslServiceImpl<H, C> {
    /// Creates the service around the hosts it runs operations against.
    #[must_use]
    pub fn new(
        host: H,
        challenges: C,
        floor: FreeSpaceFloor,
        distro: &'static dyn maran_distro::DistroAdapter,
    ) -> Self {
        Self {
            host: Arc::new(host),
            challenges: Arc::new(challenges),
            floor,
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: SslHost + 'static, C: PanelChallengeHost + 'static> SslService for SslServiceImpl<H, C> {
    /// Writes the pair into the agent's own store and rewires the site's vhost.
    async fn install_certificate(
        &self,
        request: Request<InstallCertificateRequest>,
    ) -> Result<Response<InstallCertificateResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        ) {
            Ok(site) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                // Constructed here and moved once: `CertificateMaterial`'s
                // `Debug` prints no key, and the request's own copy is dropped
                // with the rest of the message.
                let material =
                    CertificateMaterial::new(&request.certificate_pem, &request.private_key_pem);
                // The operation's own error is carried out of the blocking
                // task untouched, because `install_outcome` decides between
                // the `refused` and `error` cases from it. The only failure
                // `run_blocking` itself can produce is a task that did not
                // finish, which is an `AgentError`.
                match run_blocking(
                    "certificate operation",
                    |never: &Infallible| match *never {},
                    move || {
                        Ok(ssl::install_certificate(
                            host.as_ref(),
                            distro,
                            &site,
                            &material,
                        ))
                    },
                )
                .await
                {
                    Ok(outcome) => install_outcome(outcome),
                    Err(error) => install_certificate_response::Result::Error(error),
                }
            }
            Err(error) => install_certificate_response::Result::Error(error),
        };

        Ok(Response::new(InstallCertificateResponse {
            result: Some(result),
        }))
    }

    /// Removes the material and reverts the vhost to plain HTTP.
    async fn remove_certificate(
        &self,
        request: Request<RemoveCertificateRequest>,
    ) -> Result<Response<RemoveCertificateResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_existing_site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        ) {
            Ok(site) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("certificate operation", to_agent_error, move || {
                    ssl::remove_certificate(host.as_ref(), distro, &site)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => remove_certificate_response::Result::Ok(RemoveCertificateOk {}),
            Err(error) => remove_certificate_response::Result::Error(error),
        };

        Ok(Response::new(RemoveCertificateResponse {
            result: Some(result),
        }))
    }

    /// Generates the placeholder a site serves until a real certificate exists.
    async fn generate_self_signed(
        &self,
        request: Request<GenerateSelfSignedRequest>,
    ) -> Result<Response<GenerateSelfSignedResponse>, Status> {
        let request = request.into_inner();

        let result = match validated_site(
            &request.account_username,
            &request.domain,
            request.site.as_ref(),
        ) {
            Ok(site) => {
                let (host, distro) = (Arc::clone(&self.host), self.distro);
                run_blocking("certificate operation", to_agent_error, move || {
                    ssl::generate_self_signed(host.as_ref(), distro, &site)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(expires_at_unix) => {
                generate_self_signed_response::Result::Ok(GenerateSelfSignedOk { expires_at_unix })
            }
            Err(error) => generate_self_signed_response::Result::Error(error),
        };

        Ok(Response::new(GenerateSelfSignedResponse {
            result: Some(result),
        }))
    }

    /// Replaces the panel's own certificate and key (issue #132).
    async fn install_panel_certificate(
        &self,
        request: Request<InstallPanelCertificateRequest>,
    ) -> Result<Response<InstallPanelCertificateResponse>, Status> {
        let request = request.into_inner();
        let (host, distro) = (Arc::clone(&self.host), self.distro);
        let material = CertificateMaterial::new(&request.certificate_pem, &request.private_key_pem);

        let result = match run_blocking(
            "panel certificate operation",
            |never: &Infallible| match *never {},
            move || {
                Ok(ssl::install_panel_certificate(
                    host.as_ref(),
                    distro,
                    &material,
                ))
            },
        )
        .await
        {
            Ok(outcome) => panel_install_outcome(outcome),
            Err(error) => install_panel_certificate_response::Result::Error(error),
        };

        Ok(Response::new(InstallPanelCertificateResponse {
            result: Some(result),
        }))
    }

    /// Writes the panel's HTTP-01 challenge file, refused below the floor.
    async fn write_panel_challenge(
        &self,
        request: Request<WritePanelChallengeRequest>,
    ) -> Result<Response<WritePanelChallengeResponse>, Status> {
        let result = match ValidatedPanelChallenge::parse(&request.into_inner()) {
            Ok(challenge) => {
                let (challenges, floor) = (Arc::clone(&self.challenges), self.floor);
                run_blocking("panel challenge operation", to_agent_error, move || {
                    ssl::write_panel_challenge(
                        challenges.as_ref(),
                        &floor,
                        &challenge.token,
                        &challenge.key_authorization,
                    )
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => write_panel_challenge_response::Result::Ok(WritePanelChallengeOk {}),
            Err(error) => write_panel_challenge_response::Result::Error(error),
        };

        Ok(Response::new(WritePanelChallengeResponse {
            result: Some(result),
        }))
    }

    /// Removes the panel's HTTP-01 challenge file.
    async fn remove_panel_challenge(
        &self,
        request: Request<RemovePanelChallengeRequest>,
    ) -> Result<Response<RemovePanelChallengeResponse>, Status> {
        let result = match validated_panel_token(&request.into_inner().token) {
            Ok(token) => {
                let challenges = Arc::clone(&self.challenges);
                run_blocking("panel challenge operation", to_agent_error, move || {
                    ssl::remove_panel_challenge(challenges.as_ref(), &token)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(()) => remove_panel_challenge_response::Result::Ok(RemovePanelChallengeOk {}),
            Err(error) => remove_panel_challenge_response::Result::Error(error),
        };

        Ok(Response::new(RemovePanelChallengeResponse {
            result: Some(result),
        }))
    }
}
