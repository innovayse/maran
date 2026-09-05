//! A single IP address the firewall may ban.

use std::fmt;
use std::net::IpAddr;

use super::ban_address_error::BanAddressError;
use super::ipv4_disguise::ipv4_in_disguise;

/// A validated single IP address, in its canonical spelling.
///
/// The inner value is private and the only constructor is
/// [`BanAddress::parse`], so holding a value of this type is proof that
/// validation happened.
///
/// The address becomes an element of an nftables set, added and removed by an
/// argv the agent builds. nft parses its arguments in its own grammar, so this
/// type carries an `IpAddr` rather than the caller's text and renders the text
/// back itself: there is no spelling of a `BanAddress` the agent did not write.
///
/// The sibling [`super::source_cidr::SourceCidr`] is the other half of the same
/// idea and is deliberately a separate type. A ban is one address and a rule's
/// source is a network, the two go to different places — a set element and a
/// rule — and letting a `/0` reach the ban path would be a firewall that bans
/// the internet on one bad request.
///
/// # If you are holding an address a socket reported, map it to IPv4 first
///
/// A dual-stack listener reports an IPv4 peer in v4-MAPPED form —
/// `::ffff:203.0.113.7`, not `203.0.113.7` — unless something maps it back. This
/// type REFUSES that form, with
/// [`super::ban_address_error::BanAddressError::Ipv4InIpv6Notation`], and the
/// refusal is deliberate rather than an oversight: the firewall keeps one ban
/// set per family, so a mapped address placed in the IPv6 set would match no
/// packet an IPv4 client ever sends. Accepting it would produce a ban that
/// silently does nothing, which is worse than an error, because nobody looks
/// for the cause of an attack that was supposedly stopped.
///
/// So a caller that builds a ban out of a reported peer address — an
/// authentication failure, a rate-limit trip, anything counting by source IP —
/// must normalise it before parsing: ask the address for its mapped IPv4 form
/// and use that when it has one. A caller that did not, and got this refusal,
/// does not need to know how: the error's `as_v4` payload IS the address to
/// retry with.
///
/// `::` is not a mapped address and passes normally. `::1` is not a mapped
/// address either, but it is refused by [`BanAddress::parse`] for the separate
/// reason given there — a ban on loopback would block nothing.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct BanAddress(IpAddr);

impl BanAddress {
    /// Validates `candidate` as a single, bannable IP address and wraps it.
    ///
    /// The address is parsed by `std::net`, which is also what refuses a
    /// leading-zero octet, and then written back out and compared against the
    /// input, so an uppercase or uncompressed IPv6 address is refused with the
    /// canonical spelling in hand rather than silently accepted as a second
    /// name for an address that already has one.
    ///
    /// # Loopback is refused here, and this is the refusal that must not move
    ///
    /// A ban on `127.0.0.0/8` or `::1` cannot work: both nftables tables this
    /// agent renders accept `iif "lo"` ahead of the ban sets, so the element
    /// is added and matches nothing while every caller is told the ban was
    /// placed. The refusal lives at this parse, the last gate before the
    /// `nft` argument vector is built, because that is the only place that
    /// survives BOTH ways this can go wrong: the panel's data being wrong (a
    /// forged forwarded header, an emptied whitelist, an address the detector
    /// mis-attributed) and this agent's input being wrong (any caller at all,
    /// asking for a ban the host cannot honour). A check in the panel would
    /// survive neither — the panel's Firewall module now makes one anyway, at
    /// its own HTTP boundary, and it is an ADDITION and not a replacement:
    /// it exists so an administrator is told which mistake they made, because
    /// every reason given here reaches that administrator as one wire code
    /// shared with every other refusal. Removing this parse's refusal on the
    /// strength of that one would leave every other caller unguarded, which is
    /// the arrangement the paragraph above rules out. An intrinsic panel-side
    /// loopback whitelist was
    /// considered and rejected outright: on a TCP-bound panel a local process
    /// controls `X-Forwarded-For` and can have itself attributed to loopback,
    /// so a permanent exemption would hand exactly that attacker immunity no
    /// administrator could revoke. A refusal grants nobody anything; it only
    /// declines to record a fiction.
    ///
    /// It refuses a manual ban too, and that is the intent rather than a side
    /// effect. An administrator typing `127.0.0.1` into the ban box is asking
    /// for something this host cannot do, and a refusal naming the reason is
    /// the honest answer where a silent success is not. Nothing in this panel
    /// bans a loopback address on purpose.
    ///
    /// Taking a loopback ban back OUT is a different question and stays
    /// possible: see [`BanAddress::parse_existing`].
    ///
    /// # Errors
    ///
    /// - [`BanAddressError::Empty`] when `candidate` is empty.
    /// - [`BanAddressError::Invalid`] when it is not a single IP address — a
    ///   network, a hostname or a scoped address included.
    /// - [`BanAddressError::Ipv4InIpv6Notation`] for `::ffff:1.2.3.4` and
    ///   `::1.2.3.4`, which name an IPv4 host in IPv6 clothing and would be
    ///   added to the wrong family's ban set.
    /// - [`BanAddressError::NotCanonical`] when it is spelled some other way
    ///   than the one this type writes.
    /// - [`BanAddressError::Loopback`] for any address in `127.0.0.0/8` and
    ///   for `::1`.
    pub fn parse(candidate: &str) -> Result<Self, BanAddressError> {
        let parsed = Self::parse_existing(candidate)?;

        if parsed.0.is_loopback() {
            return Err(BanAddressError::Loopback {
                address: parsed.0.to_string(),
            });
        }

        Ok(parsed)
    }

    /// Validates `candidate` as a single IP address that a ban set may already
    /// hold, without asking whether a ban on it could ever have worked.
    ///
    /// Everything [`BanAddress::parse`] checks about the FORM of an address is
    /// checked here — it is the same code, and `parse` is this plus the
    /// loopback refusal. What is missing is that refusal, and the callers that
    /// want this one are the two that read or remove rather than add:
    /// listing what the host currently bans, and lifting a ban.
    ///
    /// The reason is that a host upgraded from a version without the loopback
    /// refusal can hold a loopback element placed by the old code. If reading
    /// a set member refused it, one such leftover would make the WHOLE ban
    /// list unreadable, and if lifting a ban refused it, that leftover could
    /// never be removed through the panel at all. Refusing to add an inert ban
    /// and refusing to clean one up are opposite things; only the first is a
    /// protection.
    ///
    /// # Errors
    ///
    /// The same as [`BanAddress::parse`], less
    /// [`BanAddressError::Loopback`], which this constructor never returns.
    pub fn parse_existing(candidate: &str) -> Result<Self, BanAddressError> {
        if candidate.is_empty() {
            return Err(BanAddressError::Empty);
        }

        let address: IpAddr = candidate.parse().map_err(|_| BanAddressError::Invalid {
            candidate: candidate.to_owned(),
        })?;

        if let IpAddr::V6(v6) = address
            && let Some(as_v4) = ipv4_in_disguise(v6)
        {
            return Err(BanAddressError::Ipv4InIpv6Notation {
                as_v4: as_v4.to_string(),
            });
        }

        let canonical = address.to_string();
        if canonical != candidate {
            return Err(BanAddressError::NotCanonical { canonical });
        }

        Ok(Self(address))
    }

    /// The validated address.
    #[must_use]
    pub fn address(&self) -> IpAddr {
        self.0
    }

    /// Whether this is an IPv4 address.
    ///
    /// The firewall keeps one ban set per family, so every caller that turns an
    /// address into a set element has to ask.
    #[must_use]
    pub fn is_v4(&self) -> bool {
        self.0.is_ipv4()
    }
}

impl fmt::Display for BanAddress {
    /// Renders the canonical address text — the exact spelling
    /// [`BanAddress::parse_existing`] accepts back. `parse` accepts it too for
    /// every value except a loopback one, which only `parse_existing` can build.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/ban_address_tests.rs"]
mod tests;
