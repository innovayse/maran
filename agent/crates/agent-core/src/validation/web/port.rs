//! A TCP or UDP port number, checked once at the boundary.

use super::port_error::PortError;

/// A validated port number in `1..=65535`.
///
/// The inner value is private and the only constructor is [`Port::parse`], so
/// holding a value of this type is proof that validation happened. It is
/// `Copy`, because a port is a number and passing one around by reference buys
/// nothing.
///
/// The type takes a `u32` and yields a `u16` on purpose: the wire carries port
/// numbers as `uint32` (protobuf has no 16-bit integer), so the out-of-range
/// value exists on the wire and has to be refused somewhere. Refusing it here
/// means every later caller holds a number that fits the field it is going into.
///
/// Deliberately absent: any notion of a reserved or privileged port. Whether a
/// port may be opened is a policy question that depends on which port the panel
/// itself is reachable on, and that is a fact only the request carries — so the
/// ruleset builder decides it, with the panel port in hand, and this type
/// answers only "is this a port number at all".
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct Port(u16);

impl Port {
    /// Validates `candidate` as a port number and wraps it.
    ///
    /// The upper bound is `u16`'s own and is checked by the conversion rather
    /// than by a constant of ours: a port IS a 16-bit field, so there is
    /// exactly one bound and one place that enforces it. A second `> 65535`
    /// check above the conversion would be a check that can never fail on its
    /// own, which is worse than no check — it masks the one that does the work
    /// and reads to a reviewer as if it were the protection.
    ///
    /// # Errors
    ///
    /// - [`PortError::Zero`] when `candidate` is `0`, which is what an absent
    ///   field decodes to and what a firewall reads as "any port".
    /// - [`PortError::TooLarge`] when `candidate` is above 65535.
    pub fn parse(candidate: u32) -> Result<Self, PortError> {
        if candidate == 0 {
            return Err(PortError::Zero);
        }

        match u16::try_from(candidate) {
            Ok(value) => Ok(Self(value)),
            Err(_) => Err(PortError::TooLarge { value: candidate }),
        }
    }

    /// The validated port number.
    #[must_use]
    pub fn value(&self) -> u16 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/port_tests.rs"]
mod tests;
