//! The shape and the bound one whitelisted php.ini setting is checked against.

/// What a whitelisted setting's value must look like, and how large it may be.
///
/// The bound travels WITH the name rather than being applied afterwards,
/// because a whitelist without bounds is not a whitelist: `memory_limit` is a
/// permitted setting at `128M` and a denial of service against every other
/// account on the machine at `64G`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OverrideKind {
    /// A php.ini byte size — `256M`, `1G`, or a plain byte count.
    ///
    /// PHP also reads `-1` here as "unlimited", which is exactly the value
    /// this bound exists to refuse; it is rejected as malformed, since the
    /// grammar below admits no sign.
    Bytes {
        /// The largest size, in bytes, the customer may set.
        maximum: u64,
    },

    /// A whole number of seconds, with a floor as well as a ceiling.
    ///
    /// The floor is not tidiness. PHP reads `0` here as "no limit", so a bound
    /// that only checked the ceiling would admit through the front door
    /// exactly what [`Self::Bytes`] refuses at `-1`, and would admit it at the
    /// setting where it costs most: a request with no execution limit holds
    /// one of the pool's fixed number of workers indefinitely.
    Seconds {
        /// The smallest number of seconds the customer may set.
        minimum: u64,
        /// The largest number of seconds the customer may set.
        maximum: u64,
    },

    /// A plain count, with a floor as well as a ceiling.
    ///
    /// `max_input_vars = 0` accepts no input variables at all, which breaks
    /// every form on the site — and does so at the next request, far from the
    /// settings page that caused it.
    Count {
        /// The smallest count the customer may set.
        minimum: u64,
        /// The largest count the customer may set.
        maximum: u64,
    },

    /// An IANA timezone name — `Europe/Yerevan`, `UTC`.
    ///
    /// Its own kind and not a free string: PHP resolves this name against the
    /// zoneinfo tree, so it is path-like, and a path-like value the agent
    /// writes into a root-owned config is checked the way every other
    /// path-like value is (rules/security.md item 2).
    Timezone,
}
