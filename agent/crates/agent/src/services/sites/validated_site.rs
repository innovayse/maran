//! Rebuilding a site's full description from what the panel restated.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::domain_error::DomainError;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_agent_core::validation::web::upstream::Upstream;
use maran_ops::sites::{CreateSiteInput, SiteCertificate, SiteKind};

use crate::proto::{AgentError, SiteBackendType, SiteSpec};
use crate::services::sites::validated_identity::validated_identity;
use crate::services::wire::invalid_input::invalid_input;

/// The most names one certificate may cover, the primary domain included:
/// Let's Encrypt refuses an order naming more than 100 identifiers ("Names per
/// Certificate", <https://letsencrypt.org/docs/rate-limits/>). The panel
/// derives its own alias bound from the same figure (`SiteAliasLimit`).
const CERTIFICATE_NAME_LIMIT: usize = 100;

/// The most aliases a site whose serving configuration is (re)rendered may
/// carry: every certificate name but the domain's own. Past it the vhost's
/// `server_name` grows with whatever a request carries.
pub(super) const MAXIMUM_ALIASES: usize = CERTIFICATE_NAME_LIMIT - 1;

/// Rebuilds the full site description from the identity and the panel's
/// [`SiteSpec`].
///
/// Every operation that re-renders a vhost must render the SAME
/// text `create_site` rendered, and that text is a function of the backend,
/// the aliases and whether a certificate is installed. Those facts are the
/// panel's: it is where a site is created, re-planned and given a certificate,
/// and the vhost on disk is a rendering of them rather than a second copy to
/// read back. So the panel restates them, and the agent revalidates every one
/// of them here before any of it reaches a template.
///
/// This is the variant for the operations that create or re-render serving
/// configuration for a name: create, enable, switch PHP version, issue and
/// install a certificate. It refuses the reserved host name set for the domain
/// and for every alias, and more aliases than `MAXIMUM_ALIASES`. Disabling
/// and removing a certificate use
/// [`super::validated_existing_site::validated_existing_site`] instead, which
/// checks shape only, so a site created under a now-reserved name — or before
/// the alias bound existed — stays manageable.
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
/// names no valid upstream, when it carries more than `MAXIMUM_ALIASES`
/// aliases, when any alias is not a valid hostname, or when the domain or any
/// alias is a reserved host name (an address literal, the loopback family, a
/// special-use suffix).
pub fn validated_site(
    account_username: &str,
    domain: &str,
    spec: Option<&SiteSpec>,
) -> Result<CreateSiteInput, AgentError> {
    let identity = validated_identity(account_username, domain)?;

    let alias_count = spec.map_or(0, |spec| spec.aliases.len());
    if alias_count > MAXIMUM_ALIASES {
        return Err(invalid_input(format!(
            "a site may carry at most {MAXIMUM_ALIASES} aliases, this one carries {alias_count}"
        )));
    }

    site_from_spec(identity, spec, Domain::parse)
}

/// Builds the site description from an already validated identity and the
/// spec, parsing each alias with `parse_alias`.
///
/// Shared by [`validated_site`] (aliases must not be reserved) and the
/// existing-site variant (aliases are only checked for shape).
///
/// # Errors
///
/// Returns the wire error when the spec is absent or any part of it is refused.
pub(super) fn site_from_spec(
    (account, domain): (AccountName, Domain),
    spec: Option<&SiteSpec>,
    parse_alias: fn(&str) -> Result<Domain, DomainError>,
) -> Result<CreateSiteInput, AgentError> {
    let spec = spec.ok_or_else(|| {
        invalid_input("the site description is required: this rpc re-renders the vhost".to_owned())
    })?;

    let aliases = spec
        .aliases
        .iter()
        .map(|alias| parse_alias(alias).map_err(|error| invalid_input(error.to_string())))
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

#[cfg(test)]
#[path = "../../tests/services/sites/validated_site_tests.rs"]
mod tests;
