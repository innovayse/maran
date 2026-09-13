//! A source network a firewall rule may be restricted to.

use std::fmt;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use super::ipv4_disguise::ipv4_in_disguise;
use super::source_cidr_error::SourceCidrError;

/// The longest prefix an IPv4 address can carry.
const MAX_IPV4_PREFIX: u8 = 32;

/// The longest prefix an IPv6 address can carry.
const MAX_IPV6_PREFIX: u8 = 128;

/// The most digits a prefix length can need — `128` is three.
const MAX_PREFIX_DIGITS: usize = 3;

/// A validated `address/prefix` source network, in its canonical spelling.
///
/// The fields are private and the only constructors are
/// [`SourceCidr::parse`] and [`SourceCidr::any_v4`], so holding a value of this
/// type is proof that validation happened.
///
/// The value is written into an nftables rule as a match on the packet's source
/// address. nft parses its own arguments in its own grammar, so a value the
/// agent had not fully accounted for would be a rule of the caller's choosing
/// in a root-loaded ruleset — which is why this type carries an `IpAddr` and a
/// prefix length rather than the caller's text, and renders the text back
/// itself. There is no spelling of a `SourceCidr` that the agent did not write.
///
/// Canonical spelling is enforced rather than normalised, for a reason that is
/// about the firewall and not about tidiness: rules are compared as text when
/// deciding whether one already exists and which one to delete, so a network
/// with two spellings is a rule that can be added under one and left behind
/// under the other.
///
/// "One spelling" is meant literally, and host bits are the part of it that is
/// easy to miss: `10.0.0.1/8` and `10.0.0.0/8` name the same network, so
/// without the host-bit refusal `10.0.0.0/8` would have 2^24 accepted
/// spellings and the sentence above would be false. Those are refused rather
/// than masked, because `203.0.113.7/24` is either one address or 256 of them
/// and choosing for the caller is choosing how much traffic to let in.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct SourceCidr {
    /// The network address.
    address: IpAddr,
    /// How many leading bits of it the rule matches on.
    prefix_length: u8,
}

impl SourceCidr {
    /// Validates `candidate` as `address/prefix` and wraps it.
    ///
    /// The address half is parsed by `std::net`, which is also what refuses a
    /// leading-zero octet — an octet like `010` is ambiguous between decimal
    /// and octal and the standard library declines to guess. The prefix half is
    /// decimal digits, bounded by the address family. Finally the parsed value
    /// is written back out and compared against the input, so the only
    /// candidate that parses is the one that is spelled the way this type
    /// spells it.
    ///
    /// # Errors
    ///
    /// - [`SourceCidrError::Empty`] when `candidate` is empty.
    /// - [`SourceCidrError::MissingPrefix`] when there is no `/`.
    /// - [`SourceCidrError::InvalidAddress`] when the address half is not an IP
    ///   address — a hostname included, because the agent resolves nothing.
    /// - [`SourceCidrError::InvalidPrefix`] when the prefix half is empty,
    ///   longer than three digits, or not decimal.
    /// - [`SourceCidrError::Ipv4InIpv6Notation`] for `::ffff:1.2.3.4` and
    ///   `::1.2.3.4`, which name an IPv4 host in IPv6 clothing and would be
    ///   rendered into the wrong family's rule.
    /// - [`SourceCidrError::PrefixTooLong`] above `/32` for IPv4 or `/128` for
    ///   IPv6.
    /// - [`SourceCidrError::HostBitsSet`] when bits below the prefix are set —
    ///   refused rather than masked, so the caller says whether they meant the
    ///   network or the single address.
    /// - [`SourceCidrError::NotCanonical`] when the value is spelled some other
    ///   way than the one this type writes.
    pub fn parse(candidate: &str) -> Result<Self, SourceCidrError> {
        if candidate.is_empty() {
            return Err(SourceCidrError::Empty);
        }

        let (address, prefix) =
            candidate
                .split_once('/')
                .ok_or_else(|| SourceCidrError::MissingPrefix {
                    candidate: candidate.to_owned(),
                })?;

        let address: IpAddr = address
            .parse()
            .map_err(|_| SourceCidrError::InvalidAddress {
                address: address.to_owned(),
            })?;

        if let IpAddr::V6(v6) = address
            && let Some(as_v4) = ipv4_in_disguise(v6)
        {
            return Err(SourceCidrError::Ipv4InIpv6Notation {
                as_v4: as_v4.to_string(),
            });
        }

        let maximum = if address.is_ipv4() {
            MAX_IPV4_PREFIX
        } else {
            MAX_IPV6_PREFIX
        };

        let prefix_length = parse_prefix(prefix, maximum)?;
        if prefix_length > maximum {
            return Err(SourceCidrError::PrefixTooLong {
                prefix: prefix.to_owned(),
                maximum,
            });
        }

        // Before the spelling check, because both spellings this refusal offers
        // are canonical by construction: a caller who fixes the host bits has
        // nothing left to fix.
        let masked = mask_to_prefix(address, prefix_length);
        if masked != address {
            return Err(SourceCidrError::HostBitsSet {
                network: Self {
                    address: masked,
                    prefix_length,
                }
                .to_string(),
                host: Self {
                    address,
                    prefix_length: maximum,
                }
                .to_string(),
            });
        }

        let network = Self {
            address,
            prefix_length,
        };

        let canonical = network.to_string();
        if canonical != candidate {
            return Err(SourceCidrError::NotCanonical { canonical });
        }

        Ok(network)
    }

    /// The network that matches every IPv4 source — `0.0.0.0/0`.
    ///
    /// Infallible and therefore a constructor of its own rather than a
    /// `parse("0.0.0.0/0")` whose `Result` every caller would have to unwrap —
    /// which the agent is not allowed to do. This is the value a rule carries
    /// when the caller asked for no source restriction at all.
    #[must_use]
    pub fn any_v4() -> Self {
        Self {
            address: IpAddr::V4(Ipv4Addr::UNSPECIFIED),
            prefix_length: 0,
        }
    }

    /// The network address.
    #[must_use]
    pub fn address(&self) -> IpAddr {
        self.address
    }

    /// How many leading bits of the address the rule matches on.
    #[must_use]
    pub fn prefix_length(&self) -> u8 {
        self.prefix_length
    }

    /// Whether this is an IPv4 network.
    ///
    /// The firewall keeps one set per family, so every caller that turns a
    /// network into a rule has to ask.
    #[must_use]
    pub fn is_v4(&self) -> bool {
        self.address.is_ipv4()
    }
}

impl fmt::Display for SourceCidr {
    /// Renders the canonical `address/prefix` text — the exact spelling
    /// [`SourceCidr::parse`] accepts back.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}/{}", self.address, self.prefix_length)
    }
}

/// Clears every bit of `address` below `prefix_length`.
///
/// The shift amount is always in range because the caller has already refused a
/// prefix longer than the family allows, and a prefix of zero is handled
/// separately rather than by shifting a whole word — `u32::MAX << 32` is
/// undefined arithmetic, not a zero mask.
fn mask_to_prefix(address: IpAddr, prefix_length: u8) -> IpAddr {
    match address {
        IpAddr::V4(v4) => {
            let mask = if prefix_length == 0 {
                0
            } else {
                u32::MAX << (u32::BITS - u32::from(prefix_length))
            };
            IpAddr::V4(Ipv4Addr::from(u32::from(v4) & mask))
        }
        IpAddr::V6(v6) => {
            let mask = if prefix_length == 0 {
                0
            } else {
                u128::MAX << (u128::BITS - u32::from(prefix_length))
            };
            IpAddr::V6(Ipv6Addr::from(u128::from(v6) & mask))
        }
    }
}

/// Reads the prefix half as a decimal number.
///
/// The digits are folded by hand rather than handed to `str::parse`, which
/// accepts a leading `+` and would let `/+8` through. The fold is checked
/// because three digits reach `999`, which a `u8` does not hold — and every
/// value that overflows it is also longer than any prefix, so the overflow and
/// the bound report the same refusal.
///
/// # Errors
///
/// - [`SourceCidrError::InvalidPrefix`] when `prefix` is empty, longer than
///   three digits, or holds anything but ASCII digits.
/// - [`SourceCidrError::PrefixTooLong`] when the digits name a number above
///   255, which no address family allows.
fn parse_prefix(prefix: &str, maximum: u8) -> Result<u8, SourceCidrError> {
    if prefix.is_empty() || prefix.len() > MAX_PREFIX_DIGITS {
        return Err(SourceCidrError::InvalidPrefix {
            prefix: prefix.to_owned(),
        });
    }

    let mut value: u8 = 0;
    for byte in prefix.bytes() {
        if !byte.is_ascii_digit() {
            return Err(SourceCidrError::InvalidPrefix {
                prefix: prefix.to_owned(),
            });
        }

        value = value
            .checked_mul(10)
            .and_then(|shifted| shifted.checked_add(byte - b'0'))
            .ok_or_else(|| SourceCidrError::PrefixTooLong {
                prefix: prefix.to_owned(),
                maximum,
            })?;
    }

    Ok(value)
}

#[cfg(test)]
#[path = "../../tests/validation/web/source_cidr_tests.rs"]
mod tests;
