//! How fast one visitor address may request a site, as nginx's `limit_req` takes it.

use super::request_rate_error::RequestRateError;

/// The largest sustained rate a site may set, in requests per second.
///
/// A site that needs more than a thousand requests a second from ONE address is
/// not being limited, it is being served; the bound also keeps the figure far
/// from any overflow when it is written as `<n>r/s`.
pub const MAXIMUM_PER_SECOND: u32 = 1000;

/// The largest burst a site may allow, in requests.
///
/// Bounded for the same reason as the rate: nginx keeps one state per address
/// in a one-mebibyte zone, and a burst far above what a page makes in a second
/// only turns the limit into a queue.
pub const MAXIMUM_BURST: u32 = 1000;

/// A validated per-address request limit: a sustained rate in `1..=1000`
/// requests per second and a burst in `0..=1000`.
///
/// The fields are private and the only constructor is [`RequestRate::parse`],
/// so holding one is proof that validation happened. Both figures are numbers
/// the template writes as `<n>r/s` and `burst=<n>`, never the caller's text:
/// a caller-supplied string would be a directive argument of the caller's
/// choosing (rules/security.md item 4).
///
/// "No limit" is the ABSENCE of this type (`Option<RequestRate>`), never a
/// value of it: a rate of 0 is refused, because on the wire 0 is what an absent
/// field decodes to and the two must not be confusable.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct RequestRate {
    /// Sustained requests per second, `1..=MAXIMUM_PER_SECOND`.
    per_second: u32,
    /// Requests allowed above the rate before refusal, `0..=MAXIMUM_BURST`.
    burst: u32,
}

impl RequestRate {
    /// Validates a rate and a burst and wraps them.
    ///
    /// # Errors
    ///
    /// - [`RequestRateError::Zero`] when `per_second` is `0`.
    /// - [`RequestRateError::RateTooHigh`] when it is above [`MAXIMUM_PER_SECOND`].
    /// - [`RequestRateError::BurstTooHigh`] when `burst` is above [`MAXIMUM_BURST`].
    pub fn parse(per_second: u32, burst: u32) -> Result<Self, RequestRateError> {
        if per_second == 0 {
            return Err(RequestRateError::Zero);
        }
        if per_second > MAXIMUM_PER_SECOND {
            return Err(RequestRateError::RateTooHigh {
                value: per_second,
                maximum: MAXIMUM_PER_SECOND,
            });
        }
        if burst > MAXIMUM_BURST {
            return Err(RequestRateError::BurstTooHigh {
                value: burst,
                maximum: MAXIMUM_BURST,
            });
        }

        Ok(Self { per_second, burst })
    }

    /// The sustained rate, in requests per second.
    #[must_use]
    pub fn per_second(&self) -> u32 {
        self.per_second
    }

    /// The burst, in requests above the sustained rate.
    #[must_use]
    pub fn burst(&self) -> u32 {
        self.burst
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/request_rate_tests.rs"]
mod tests;
