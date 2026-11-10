//! Why a site's options were refused.

/// Reasons the options of a site are refused before anything is rendered.
///
/// Every variant is the caller's input being wrong, decided with no host
/// touched. The text is for an operator's log; the panel decides what a
/// customer is told.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SiteOptionsError {
    /// A prefix rule's path does not end with `/`.
    #[error("the path of a prefix redirect must end with `/`")]
    PrefixPathNotDirectory,

    /// A prefix rule's target does not end with `/`, so the rest of the request
    /// path appended to it would run into its last segment.
    #[error("the target of a prefix redirect must end with `/`")]
    PrefixTargetNotDirectory,

    /// The site has more redirect rules than the limit.
    #[error("a site may have at most {maximum} redirect rules, this one has {count}")]
    TooManyRedirects {
        /// How many were offered.
        count: usize,
        /// The most allowed.
        maximum: usize,
    },

    /// Two redirect rules have the same path and the same match, which nginx
    /// refuses as a duplicate `location`.
    #[error("two redirect rules have the same path and match")]
    DuplicateRedirect,

    /// The site has more basic-authentication users than the limit.
    #[error("a site may have at most {maximum} basic-authentication users, this one has {count}")]
    TooManyUsers {
        /// How many were offered.
        count: usize,
        /// The most allowed.
        maximum: usize,
    },

    /// Two basic-authentication users have the same name.
    #[error("two basic-authentication users have the same name")]
    DuplicateUser,

    /// The IP list holds no network.
    ///
    /// An allow-list of nothing would lock everyone out and a deny-list of
    /// nothing would do nothing; both are a request the caller did not mean, so
    /// "no IP rule" is the absence of the list and not an empty one.
    #[error("an IP list needs at least one network")]
    EmptyIpList,

    /// The IP list has more networks than the limit.
    #[error("an IP list may have at most {maximum} networks, this one has {count}")]
    TooManyNetworks {
        /// How many were offered.
        count: usize,
        /// The most allowed.
        maximum: usize,
    },

    /// A network appears twice in the IP list.
    #[error("a network appears twice in the IP list")]
    DuplicateNetwork,

    /// The canonical host named is not one of the names the site serves.
    #[error("the canonical host `{wanted}` is not served by the site")]
    CanonicalHostNotServed {
        /// The name the option would make canonical.
        wanted: String,
    },

    /// A request-body ceiling was set for a static site, which accepts no body.
    #[error("a static site accepts no request body, so it has no upload size")]
    BodySizeOnStaticSite,

    /// A cache rule was set for a reverse-proxied site, whose responses come
    /// from the upstream and not from files.
    #[error("a reverse-proxied site serves no files, so it has no static cache rule")]
    CacheOnProxiedSite,

    /// The site asks for a per-address request limit and the host's site web server is Apache.
    ///
    /// Apache's stock modules have no such limit, so the request is refused where it is made
    /// instead of being rendered without it: a limit the visitor was promised and the server
    /// does not enforce is worse than a refusal.
    #[error("a request limit needs nginx as the site web server")]
    RequestRateOnApache,
}
