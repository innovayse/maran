//! The per-site options a vhost is rendered with (issue #151).

use maran_agent_core::validation::web::client_body_size::ClientBodySize;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::request_rate::RequestRate;
use maran_agent_core::validation::web::static_cache_days::StaticCacheDays;
use maran_distro::SiteWebServer;

use crate::sites::model::basic_auth_user::BasicAuthUser;
use crate::sites::model::canonical_host::CanonicalHost;
use crate::sites::model::canonical_split::CanonicalSplit;
use crate::sites::model::ip_access::IpAccess;
use crate::sites::model::redirect_rule::RedirectRule;
use crate::sites::model::site_kind::SiteKind;
use crate::sites::model::site_options_error::SiteOptionsError;

/// The most redirect rules one site may have.
pub const MAXIMUM_REDIRECTS: usize = 25;

/// The most basic-authentication users one site may have.
pub const MAXIMUM_USERS: usize = 10;

/// The prefix a site's `www` name carries.
const WWW_PREFIX: &str = "www.";

/// Everything optional about how one site is served: its canonical host,
/// redirects, basic authentication, IP rules, request limit, upload size and
/// cache rule.
///
/// Every part is a validated type, so none of them can hold a character that
/// would end an nginx directive (rules/security.md item 4). What this type adds
/// is the checks that need more than one part: the caps, the duplicates, and
/// [`Self::check_against`], which is about the site the options are for.
///
/// `SiteOptions::default()` is a site with none, and it renders the same vhost
/// the agent rendered before options existed. The panel sends the options on
/// every re-render, so a vhost rewritten for an unrelated reason (a PHP
/// version, a certificate) keeps them; this type is what `CreateSiteInput`
/// carries for that.
///
/// Fields are private and the only constructors are [`SiteOptions::new`] and
/// `Default`; the request limit (issue #159) is added to either with
/// [`SiteOptions::with_request_rate`], because it has no relation to the other
/// parts that [`SiteOptions::new`] would have to check.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct SiteOptions {
    /// Which of the apex / `www` pair visitors end up on.
    canonical_host: Option<CanonicalHost>,
    /// The largest request body, when set.
    client_max_body_size: Option<ClientBodySize>,
    /// Days a browser may cache static files, when set.
    static_cache_days: Option<StaticCacheDays>,
    /// The redirect rules, in the order they are written.
    redirects: Vec<RedirectRule>,
    /// The basic-authentication users; empty means no authentication.
    basic_auth: Vec<BasicAuthUser>,
    /// The IP rules, when the site has any.
    ip_access: Option<IpAccess>,
    /// How fast one visitor address may request the site; `None` is no limit.
    request_rate: Option<RequestRate>,
}

impl SiteOptions {
    /// Builds a site's options from validated parts.
    ///
    /// # Errors
    ///
    /// - [`SiteOptionsError::TooManyRedirects`] beyond [`MAXIMUM_REDIRECTS`].
    /// - [`SiteOptionsError::DuplicateRedirect`] when two rules have the same
    ///   path and match.
    /// - [`SiteOptionsError::TooManyUsers`] beyond [`MAXIMUM_USERS`].
    /// - [`SiteOptionsError::DuplicateUser`] when two users share a name.
    pub fn new(
        canonical_host: Option<CanonicalHost>,
        client_max_body_size: Option<ClientBodySize>,
        static_cache_days: Option<StaticCacheDays>,
        redirects: Vec<RedirectRule>,
        basic_auth: Vec<BasicAuthUser>,
        ip_access: Option<IpAccess>,
    ) -> Result<Self, SiteOptionsError> {
        if redirects.len() > MAXIMUM_REDIRECTS {
            return Err(SiteOptionsError::TooManyRedirects {
                count: redirects.len(),
                maximum: MAXIMUM_REDIRECTS,
            });
        }
        if redirects.iter().enumerate().any(|(index, rule)| {
            redirects[..index].iter().any(|earlier| {
                earlier.path() == rule.path() && earlier.matching() == rule.matching()
            })
        }) {
            return Err(SiteOptionsError::DuplicateRedirect);
        }
        if basic_auth.len() > MAXIMUM_USERS {
            return Err(SiteOptionsError::TooManyUsers {
                count: basic_auth.len(),
                maximum: MAXIMUM_USERS,
            });
        }
        if basic_auth.iter().enumerate().any(|(index, user)| {
            basic_auth[..index]
                .iter()
                .any(|earlier| earlier.name() == user.name())
        }) {
            return Err(SiteOptionsError::DuplicateUser);
        }

        Ok(Self {
            canonical_host,
            client_max_body_size,
            static_cache_days,
            redirects,
            basic_auth,
            ip_access,
            request_rate: None,
        })
    }

    /// Returns these options with the per-address request limit set to
    /// `request_rate`; `None` removes the limit.
    ///
    /// The limit is checked by its own type ([`RequestRate::parse`]) and has no
    /// relation to the other parts, so it is set here and not through
    /// [`SiteOptions::new`], whose checks are all between the other parts.
    #[must_use]
    pub fn with_request_rate(mut self, request_rate: Option<RequestRate>) -> Self {
        self.request_rate = request_rate;
        self
    }

    /// Which of the apex / `www` pair visitors end up on, when set.
    #[must_use]
    pub fn canonical_host(&self) -> Option<CanonicalHost> {
        self.canonical_host
    }

    /// The largest request body, when set.
    #[must_use]
    pub fn client_max_body_size(&self) -> Option<ClientBodySize> {
        self.client_max_body_size
    }

    /// Days a browser may cache static files, when set.
    #[must_use]
    pub fn static_cache_days(&self) -> Option<StaticCacheDays> {
        self.static_cache_days
    }

    /// The redirect rules, in the order they are written.
    #[must_use]
    pub fn redirects(&self) -> &[RedirectRule] {
        &self.redirects
    }

    /// The basic-authentication users; empty means no authentication.
    #[must_use]
    pub fn basic_auth(&self) -> &[BasicAuthUser] {
        &self.basic_auth
    }

    /// The IP rules, when the site has any.
    #[must_use]
    pub fn ip_access(&self) -> Option<&IpAccess> {
        self.ip_access.as_ref()
    }

    /// How fast one visitor address may request the site; `None` is no limit.
    #[must_use]
    pub fn request_rate(&self) -> Option<RequestRate> {
        self.request_rate
    }

    /// Checks the options against the site they are for.
    ///
    /// # Errors
    ///
    /// - [`SiteOptionsError::BodySizeOnStaticSite`] for an upload size on a
    ///   static site, which accepts no request body.
    /// - [`SiteOptionsError::CacheOnProxiedSite`] for a cache rule on a
    ///   reverse-proxied site, whose responses come from the upstream.
    /// - [`SiteOptionsError::CanonicalHostNotServed`] when the canonical host
    ///   is not one of the site's names.
    pub fn check_against(
        &self,
        kind: &SiteKind,
        domain: &Domain,
        aliases: &[Domain],
    ) -> Result<(), SiteOptionsError> {
        match kind {
            SiteKind::Static if self.client_max_body_size.is_some() => {
                return Err(SiteOptionsError::BodySizeOnStaticSite);
            }
            SiteKind::ReverseProxy { .. } | SiteKind::Node { .. }
                if self.static_cache_days.is_some() =>
            {
                return Err(SiteOptionsError::CacheOnProxiedSite);
            }
            SiteKind::Static
            | SiteKind::ReverseProxy { .. }
            | SiteKind::Node { .. }
            | SiteKind::Php { .. } => {}
        }

        self.canonical_split(domain, aliases).map(|_| ())
    }

    /// Checks the options against the web server that will serve the site.
    ///
    /// # Errors
    ///
    /// - [`SiteOptionsError::RequestRateOnApache`] for a request limit on an Apache host.
    pub fn check_web_server(&self, web_server: SiteWebServer) -> Result<(), SiteOptionsError> {
        match web_server {
            SiteWebServer::Apache if self.request_rate.is_some() => {
                Err(SiteOptionsError::RequestRateOnApache)
            }
            SiteWebServer::Apache | SiteWebServer::Nginx => Ok(()),
        }
    }

    /// Divides the site's names between the canonical host and the names that
    /// redirect to it, or answers `None` when there is nothing to redirect.
    ///
    /// The apex is the domain with one leading `www.` removed and the `www`
    /// name is `www.` followed by the apex, so a site registered as
    /// `www.example.com` and one registered as `example.com` make the same
    /// pair. When the other name of the pair is not one the site serves there
    /// is nothing to redirect and the answer is `None`; the canonical name
    /// itself must be served.
    ///
    /// # Errors
    ///
    /// Returns [`SiteOptionsError::CanonicalHostNotServed`] when the canonical
    /// name is not the domain or an alias.
    pub fn canonical_split(
        &self,
        domain: &Domain,
        aliases: &[Domain],
    ) -> Result<Option<CanonicalSplit>, SiteOptionsError> {
        let Some(choice) = self.canonical_host else {
            return Ok(None);
        };

        let apex = domain
            .as_str()
            .strip_prefix(WWW_PREFIX)
            .unwrap_or_else(|| domain.as_str())
            .to_owned();
        let www = format!("{WWW_PREFIX}{apex}");
        let (canonical, other) = match choice {
            CanonicalHost::Www => (www, apex),
            CanonicalHost::Apex => (apex, www),
        };

        let names: Vec<&str> = std::iter::once(domain)
            .chain(aliases)
            .map(Domain::as_str)
            .collect();
        if !names.contains(&canonical.as_str()) {
            return Err(SiteOptionsError::CanonicalHostNotServed { wanted: canonical });
        }
        if !names.contains(&other.as_str()) {
            return Ok(None);
        }

        let served = std::iter::once(canonical.as_str())
            .chain(
                names
                    .iter()
                    .copied()
                    .filter(|name| *name != canonical && *name != other),
            )
            .map(str::to_owned)
            .collect();

        Ok(Some(CanonicalSplit {
            canonical,
            served,
            redirected: vec![other],
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/sites/model/site_options_tests.rs"]
mod tests;
