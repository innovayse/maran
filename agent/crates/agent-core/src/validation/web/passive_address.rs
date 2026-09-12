//! The public address vsftpd advertises for a passive data connection.

use std::net::Ipv4Addr;

use super::passive_address_error::PassiveAddressError;

/// A validated IPv4 address for vsftpd's `pasv_address`.
///
/// The inner value is private and [`PassiveAddress::parse`] is the only
/// constructor, so holding one is proof that validation happened.
///
/// # What this refuses, and why the type exists
///
/// This is the ONE value in the FTPS subsystem an operator types freehand, and
/// it is written verbatim into `vsftpd.conf`, which is a line-oriented
/// `key=value` file read by a daemon that starts as root. A newline in it
/// appends a directive of somebody else's choosing to that file — the panel's
/// equivalent of SQL injection (rules/security.md §4) — and the directives on
/// offer are not cosmetic: `chroot_local_user=NO` alone turns every FTPS login
/// into a session over the whole filesystem.
///
/// The refusal is not a character sweep that somebody has to keep complete. The
/// text is parsed by [`Ipv4Addr`], whose own grammar is exactly four decimal
/// octets and nothing else, so the accepted set is one dotted quad in the one
/// spelling this type would itself have written. That makes the refusal a
/// property of the grammar rather than of a list, and it is what refuses, in one
/// clause and without a character loop:
///
/// - `203.0.113.7\npasv_min_port=1` and anything else carrying a control
///   character — the injection this type exists for;
/// - a host name (`ftp.example.com`), which vsftpd would not resolve, and which
///   would make the advertised address depend on a resolver;
/// - a truncated quad (`203.0.113`) and an out-of-range octet (`203.0.113.256`);
/// - a leading-zero octet (`010.0.0.1`), which some resolvers read as octal and
///   others as decimal — two different hosts behind one spelling;
/// - surrounding whitespace (` 203.0.113.7`) at either end;
/// - the empty string.
///
/// # There is no canonical-spelling re-check here, and that is measured
///
/// The sibling [`super::ban_address::BanAddress`] renders its parsed address
/// back out and compares it against the input, because IPv6 has many spellings
/// of one address. IPv4 as `std` parses it has none: measured on this
/// toolchain, every candidate `Ipv4Addr::from_str` ACCEPTS is already
/// byte-identical to its own `to_string()` — `010.0.0.1`, `203.0.113.07`,
/// `0x7f.0.0.1` and both whitespace-padded forms are all rejected by the parser
/// itself. A round-trip comparison here would therefore be a branch no input
/// can take, and a defensive call that cannot fail is deleted rather than
/// labelled (rules/testing.md). What replaces it is a test: the refusal of
/// `010.0.0.1` is asserted by name, so a future standard library that relaxed
/// its parser would turn that test red rather than silently widen what this
/// panel writes into a root daemon's configuration.
///
/// # Why IPv4 only
///
/// `pasv_address` is an IPv4-only directive: the PASV reply format (RFC 959)
/// carries four decimal octets and has no IPv6 form, and vsftpd's IPv6 answer
/// is EPSV, which advertises a port and no address at all. Accepting an IPv6
/// address here would produce a configuration the daemon refuses to start on,
/// or a PASV reply that names an address no client can reach — a failure that
/// only appears when a customer first transfers a file. The refusal is
/// deliberate and is not an omission, which is why it has its own error
/// variant, [`PassiveAddressError::NotIpv4`], instead of being folded into a
/// general "invalid".
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct PassiveAddress(String);

impl PassiveAddress {
    /// Validates `candidate` as the IPv4 address vsftpd may advertise.
    ///
    /// # Errors
    ///
    /// - [`PassiveAddressError::Empty`] when `candidate` is empty.
    /// - [`PassiveAddressError::NotIpv4`] when it parses as an IPv6 address,
    ///   which `pasv_address` has no use for.
    /// - [`PassiveAddressError::Invalid`] when it is not an IPv4 address at
    ///   all — a host name, a truncated quad, an out-of-range octet, a value
    ///   carrying a newline or any other control character, a leading-zero
    ///   octet, and a value padded with whitespace.
    pub fn parse(candidate: &str) -> Result<Self, PassiveAddressError> {
        if candidate.is_empty() {
            return Err(PassiveAddressError::Empty);
        }

        // Asked of the IPv6 parser first, so an operator who typed an address
        // of the wrong family is told THAT, rather than being told their
        // address is not an address.
        if candidate.parse::<std::net::Ipv6Addr>().is_ok() {
            return Err(PassiveAddressError::NotIpv4 {
                candidate: candidate.to_owned(),
            });
        }

        let address: Ipv4Addr = candidate
            .parse()
            .map_err(|_| PassiveAddressError::Invalid {
                candidate: candidate.to_owned(),
            })?;

        // Rendered from the parsed address rather than kept from the input, so
        // the stored text is one this type wrote. On this toolchain the two are
        // always the same string — see the type's own note on why there is no
        // comparison here — and rendering is what keeps that true if they ever
        // stop being.
        Ok(Self(address.to_string()))
    }

    /// The address in the canonical spelling this type accepts back.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/passive_address_tests.rs"]
mod tests;
