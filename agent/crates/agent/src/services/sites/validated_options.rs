//! Revalidating a site's per-site options against the agent's own types.

use maran_agent_core::validation::web::basic_auth_user_name::BasicAuthUserName;
use maran_agent_core::validation::web::bcrypt_hash::BcryptHash;
use maran_agent_core::validation::web::client_body_size::ClientBodySize;
use maran_agent_core::validation::web::redirect_path::RedirectPath;
use maran_agent_core::validation::web::redirect_target::RedirectTarget;
use maran_agent_core::validation::web::request_rate::RequestRate;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_agent_core::validation::web::static_cache_days::StaticCacheDays;
use maran_ops::sites::SiteOptions;
use maran_ops::sites::model::basic_auth_user::BasicAuthUser;
use maran_ops::sites::model::canonical_host::CanonicalHost;
use maran_ops::sites::model::ip_access::IpAccess;
use maran_ops::sites::model::ip_access_mode::IpAccessMode;
use maran_ops::sites::model::redirect_match::RedirectMatch;
use maran_ops::sites::model::redirect_rule::RedirectRule;
use maran_ops::sites::model::redirect_status::RedirectStatus;
use maran_ops::sites::model::site_options::{MAXIMUM_REDIRECTS, MAXIMUM_USERS};

use crate::proto::{
    AgentError, BasicAuthUser as WireUser, CanonicalHost as WireCanonicalHost,
    IpAccess as WireIpAccess, IpAccessMode as WireIpAccessMode, RedirectMatch as WireMatch,
    RedirectRule as WireRule, SiteOptions as WireOptions,
};
use crate::services::wire::invalid_input::invalid_input;

/// The wire spelling of a permanent redirect.
const STATUS_PERMANENT: u32 = 301;

/// The wire spelling of a temporary redirect.
const STATUS_TEMPORARY: u32 = 302;

/// Revalidates a site's options against the agent's own types.
///
/// The panel validates the same options before it stores them. This is the
/// agent's copy, and it is the one that decides (rules/security.md item 1):
/// every value here ends up in a file nginx reads as root, so the panel having
/// sent it is not why it is safe. Every text goes through the validated type
/// that cannot hold a character able to end an nginx directive, and the caps
/// are checked before anything is parsed so a hostile message cannot make the
/// agent do unbounded work.
///
/// An absent message is a site with no options, which is what a caller that
/// predates the field sends. Every zero value inside the message means
/// "absent" too, with two exceptions that are refused instead of read as
/// absent: a basic-authentication message with no users, and an IP list that
/// names a mode but no network (or networks but no mode). Both are a request
/// the caller did not mean, and silently dropping them would leave a site
/// open that its owner meant to close.
///
/// The password hash is never echoed: a refusal names the user's position or
/// the rule that broke, not the hash.
///
/// # Errors
///
/// Returns the wire error for an unknown canonical host, match, mode or
/// status, any value its validated type refuses, a cap exceeded, and the
/// combinations [`SiteOptions::new`] refuses (duplicate redirects, users or
/// networks, a prefix rule whose path or target does not end with `/`).
pub fn validated_options(wire: Option<&WireOptions>) -> Result<SiteOptions, AgentError> {
    let Some(wire) = wire else {
        return Ok(SiteOptions::default());
    };

    if wire.redirects.len() > MAXIMUM_REDIRECTS {
        return Err(invalid_input(format!(
            "a site may have at most {MAXIMUM_REDIRECTS} redirect rules, this one has {}",
            wire.redirects.len()
        )));
    }

    let canonical_host = match WireCanonicalHost::try_from(wire.canonical_host) {
        Ok(WireCanonicalHost::Unspecified) => None,
        Ok(WireCanonicalHost::Www) => Some(CanonicalHost::Www),
        Ok(WireCanonicalHost::Apex) => Some(CanonicalHost::Apex),
        Err(_) => {
            return Err(invalid_input(format!(
                "unknown canonical host {}",
                wire.canonical_host
            )));
        }
    };
    let client_max_body_size = (wire.client_max_body_size_mib != 0)
        .then(|| ClientBodySize::parse(wire.client_max_body_size_mib))
        .transpose()
        .map_err(|error| invalid_input(error.to_string()))?;
    let static_cache_days = (wire.static_cache_days != 0)
        .then(|| StaticCacheDays::parse(wire.static_cache_days))
        .transpose()
        .map_err(|error| invalid_input(error.to_string()))?;
    let redirects = wire
        .redirects
        .iter()
        .map(validated_rule)
        .collect::<Result<Vec<_>, _>>()?;
    let basic_auth = validated_users(wire)?;
    let ip_access = wire
        .ip_access
        .as_ref()
        .map(validated_ip_access)
        .transpose()?;

    let request_rate = validated_request_rate(wire)?;

    SiteOptions::new(
        canonical_host,
        client_max_body_size,
        static_cache_days,
        redirects,
        basic_auth,
        ip_access.flatten(),
    )
    .map(|options| options.with_request_rate(request_rate))
    .map_err(|error| invalid_input(error.to_string()))
}

/// Revalidates the per-address request limit.
///
/// A rate of 0 is no limit, which is how a caller that predates the field (or
/// wants none) says so. A burst with no rate is refused and not read as "no
/// limit": the caller named a burst they expect to take effect, and dropping
/// it silently would leave a site unthrottled that its owner meant to throttle.
///
/// # Errors
///
/// Returns the wire error for a burst with no rate, and any value
/// [`RequestRate`] refuses.
fn validated_request_rate(wire: &WireOptions) -> Result<Option<RequestRate>, AgentError> {
    if wire.request_rate_per_second == 0 {
        return if wire.request_burst == 0 {
            Ok(None)
        } else {
            Err(invalid_input(
                "a request burst needs a request rate; send neither to remove the limit".to_owned(),
            ))
        };
    }

    RequestRate::parse(wire.request_rate_per_second, wire.request_burst)
        .map(Some)
        .map_err(|error| invalid_input(error.to_string()))
}

/// Revalidates one redirect rule.
///
/// # Errors
///
/// Returns the wire error for an unknown match or status, or a path or target
/// its type refuses, or a prefix rule that does not end with `/`.
fn validated_rule(rule: &WireRule) -> Result<RedirectRule, AgentError> {
    let matching = match WireMatch::try_from(rule.r#match) {
        Ok(WireMatch::Exact) => RedirectMatch::Exact,
        Ok(WireMatch::Prefix) => RedirectMatch::Prefix,
        Ok(WireMatch::Unspecified) | Err(_) => {
            return Err(invalid_input(format!(
                "unknown redirect match {}",
                rule.r#match
            )));
        }
    };
    let status = match rule.status {
        STATUS_PERMANENT => RedirectStatus::Permanent,
        STATUS_TEMPORARY => RedirectStatus::Temporary,
        other => {
            return Err(invalid_input(format!(
                "a redirect status is 301 or 302, not {other}"
            )));
        }
    };

    RedirectRule::new(
        RedirectPath::parse(&rule.path).map_err(|error| invalid_input(error.to_string()))?,
        matching,
        RedirectTarget::parse(&rule.target).map_err(|error| invalid_input(error.to_string()))?,
        status,
    )
    .map_err(|error| invalid_input(error.to_string()))
}

/// Revalidates the basic-authentication users, if there are any.
///
/// # Errors
///
/// Returns the wire error for a message with no users, more users than the
/// cap, a name or hash its type refuses.
fn validated_users(wire: &WireOptions) -> Result<Vec<BasicAuthUser>, AgentError> {
    let Some(auth) = &wire.basic_auth else {
        return Ok(Vec::new());
    };
    if auth.users.is_empty() {
        return Err(invalid_input(
            "a basic-authentication message needs at least one user; send none to turn it off"
                .to_owned(),
        ));
    }
    if auth.users.len() > MAXIMUM_USERS {
        return Err(invalid_input(format!(
            "a site may have at most {MAXIMUM_USERS} basic-authentication users, this one has {}",
            auth.users.len()
        )));
    }

    auth.users.iter().map(validated_user).collect()
}

/// Revalidates one user. The error never carries the hash.
///
/// # Errors
///
/// Returns the wire error for a name or a hash its type refuses.
fn validated_user(user: &WireUser) -> Result<BasicAuthUser, AgentError> {
    let name =
        BasicAuthUserName::parse(&user.name).map_err(|error| invalid_input(error.to_string()))?;
    let hash = BcryptHash::parse(&user.password_hash).map_err(|error| {
        invalid_input(format!(
            "the password hash of user `{}` is refused: {error}",
            name.as_str()
        ))
    })?;

    Ok(BasicAuthUser::new(name, hash))
}

/// Revalidates an IP list. An unset mode with no networks is no list at all
/// (`None`), which is how a caller says "no IP rule".
///
/// # Errors
///
/// Returns the wire error for a mode with no networks, networks with no mode,
/// an unknown mode, a network its type refuses, more networks than the cap, or
/// a duplicate.
fn validated_ip_access(wire: &WireIpAccess) -> Result<Option<IpAccess>, AgentError> {
    let mode = match WireIpAccessMode::try_from(wire.mode) {
        Ok(WireIpAccessMode::AllowList) => Some(IpAccessMode::AllowList),
        Ok(WireIpAccessMode::DenyList) => Some(IpAccessMode::DenyList),
        Ok(WireIpAccessMode::Unspecified) => None,
        Err(_) => {
            return Err(invalid_input(format!("unknown IP list mode {}", wire.mode)));
        }
    };

    let Some(mode) = mode else {
        return if wire.cidrs.is_empty() {
            Ok(None)
        } else {
            Err(invalid_input(
                "an IP list with networks needs a mode".to_owned(),
            ))
        };
    };

    let networks = wire
        .cidrs
        .iter()
        .map(|cidr| SourceCidr::parse(cidr).map_err(|error| invalid_input(error.to_string())))
        .collect::<Result<Vec<_>, _>>()?;

    IpAccess::new(mode, networks)
        .map(Some)
        .map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/sites/validated_options_tests.rs"]
mod tests;
