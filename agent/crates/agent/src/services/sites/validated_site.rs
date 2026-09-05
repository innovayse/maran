//! Rebuilding a site's full description from what the panel restated.

use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_agent_core::validation::web::upstream::Upstream;
use maran_ops::sites::{CreateSiteInput, SiteCertificate, SiteKind};

use crate::proto::{AgentError, SiteBackendType, SiteSpec};
use crate::services::sites::validated_identity::validated_identity;
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the full site description from the identity and the panel's
/// [`SiteSpec`].
///
/// Every operation that re-renders a vhost — enabling, disabling, switching
/// PHP version, installing or removing a certificate — must render the SAME
/// text `create_site` rendered, and that text is a function of the backend,
/// the aliases and whether a certificate is installed. Those facts are the
/// panel's: it is where a site is created, re-planned and given a certificate,
/// and the vhost on disk is a rendering of them rather than a second copy to
/// read back. So the panel restates them, and the agent revalidates every one
/// of them here before any of it reaches a template.
///
/// An absent spec is refused rather than defaulted. proto3 cannot tell
/// "absent" from "static with no aliases", so accepting the default would let
/// an old or a buggy caller replace a live PHP site's vhost with a static one
/// and take its TLS block with it.
///
/// # Errors
///
/// Returns the wire error when the spec is absent, when its backend is
/// unspecified, when a PHP site names no valid version, when a proxied site
/// names no valid upstream, or when any alias is not a valid hostname.
pub fn validated_site(
    account_username: &str,
    domain: &str,
    spec: Option<&SiteSpec>,
) -> Result<CreateSiteInput, AgentError> {
    let (account, domain) = validated_identity(account_username, domain)?;

    let spec = spec.ok_or_else(|| {
        invalid_input("the site description is required: this rpc re-renders the vhost".to_owned())
    })?;

    let aliases = spec
        .aliases
        .iter()
        .map(|alias| Domain::parse(alias).map_err(|error| invalid_input(error.to_string())))
        .collect::<Result<Vec<Domain>, AgentError>>()?;

    let kind = match SiteBackendType::try_from(spec.backend_type) {
        Ok(SiteBackendType::Static) => SiteKind::Static,
        Ok(SiteBackendType::Php) => SiteKind::Php {
            version: PhpVersion::parse(&spec.php_version)
                .map_err(|error| invalid_input(error.to_string()))?,
        },
        Ok(SiteBackendType::ReverseProxy) => SiteKind::ReverseProxy {
            upstream: Upstream::parse(&spec.proxy_upstream)
                .map_err(|error| invalid_input(error.to_string()))?,
        },
        // Unspecified and any value this build does not know are the same
        // answer: the agent will not guess what a site serves.
        Ok(SiteBackendType::Unspecified) | Err(_) => {
            return Err(invalid_input(format!(
                "unknown site backend type {}",
                spec.backend_type
            )));
        }
    };

    // Derived from the domain, never supplied: `SiteCertificate` has one
    // constructor and it puts both files in the agent's own directory. All the
    // caller decides is whether there is one.
    let certificate = spec
        .has_certificate
        .then(|| SiteCertificate::for_domain(&domain));

    Ok(CreateSiteInput {
        account,
        domain,
        aliases,
        kind,
        certificate,
    })
}
