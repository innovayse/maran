//! `SslService`: certificate material on disk, and the vhost that points at it.

use std::sync::Arc;

use maran_ops::ssl::{self, CertificateMaterial, SslHost};
use tonic::{Request, Response, Status};

use crate::proto::ssl_service_server::SslService;
use crate::proto::{
    GenerateSelfSignedOk, GenerateSelfSignedRequest, GenerateSelfSignedResponse,
    InstallCertificateOk, InstallCertificateRequest, InstallCertificateResponse,
    RemoveCertificateOk, RemoveCertificateRequest, RemoveCertificateResponse,
    generate_self_signed_response, install_certificate_response, remove_certificate_response,
};
use crate::services::sites::validated_site::validated_site;
use crate::services::ssl::ssl_status::to_agent_error;
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
pub struct SslServiceImpl<H> {
    /// The machine the certificate operations run against.
    host: Arc<H>,
    /// Where platform facts come from — the openssl binary, the nginx binary
    /// and the service manager. A service never branches on a distribution
    /// itself (rules/rust.md "Distro adapter"); it passes this on.
    distro: &'static dyn maran_distro::DistroAdapter,
}

impl<H: SslHost + 'static> SslServiceImpl<H> {
    /// Creates the service around the host it runs operations against.
    #[must_use]
    pub fn new(host: H, distro: &'static dyn maran_distro::DistroAdapter) -> Self {
        Self {
            host: Arc::new(host),
            distro,
        }
    }
}

#[tonic::async_trait]
impl<H: SslHost + 'static> SslService for SslServiceImpl<H> {
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
                run_blocking("certificate operation", to_agent_error, move || {
                    ssl::install_certificate(host.as_ref(), distro, &site, &material)
                })
                .await
            }
            Err(error) => Err(error),
        };

        let result = match result {
            Ok(expires_at_unix) => {
                install_certificate_response::Result::Ok(InstallCertificateOk { expires_at_unix })
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

        let result = match validated_site(
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
}
