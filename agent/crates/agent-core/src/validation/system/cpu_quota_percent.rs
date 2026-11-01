//! A share of CPU time, in percent of ONE CPU, that an account's slice may use.

use super::cpu_quota_percent_error::CpuQuotaPercentError;

/// The largest quota accepted: 1024 whole CPUs. systemd itself takes any percentage and the kernel
/// clamps nothing, so this is an overflow-and-typo guard rather than a statement about any host.
pub const MAXIMUM: u32 = 102_400;

/// A share of CPU time, in percent of ONE CPU, that an account's slice may use.
///
/// It becomes systemd's `CPUQuota=` on the account's `user-<uid>.slice`, which
/// means "percent of one CPU": 50 is half a core, 200 is two whole cores. The
/// kernel enforces it through the cgroup v2 `cpu.max` quota, so a process
/// spinning in a loop is throttled to it rather than merely deprioritised.
///
/// The inner value is private and [`CpuQuotaPercent::parse`] is the only constructor, so
/// holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" is a separate, typed answer the caller states on purpose (the
/// accounts area's `ResourceCeiling::Unlimited`), never a number.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct CpuQuotaPercent(u32);

impl CpuQuotaPercent {
    /// Validates `candidate` as a CPU quota and wraps it.
    ///
    /// # Errors
    ///
    /// - [`CpuQuotaPercentError::Zero`] when `candidate` is `0`, which is what an absent
    ///   field decodes to and which would otherwise read as a ceiling of nothing.
    /// - [`CpuQuotaPercentError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, CpuQuotaPercentError> {
        if candidate == 0 {
            return Err(CpuQuotaPercentError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(CpuQuotaPercentError::TooLarge {
                value: candidate,
                maximum: MAXIMUM,
            });
        }

        Ok(Self(candidate))
    }

    /// The validated figure.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }

    /// The `CPUQuota=` assignment `systemctl set-property` is handed for this
    /// figure.
    ///
    /// Built from a validated integer and a fixed key, so the text can carry
    /// nothing but digits after the `=`: no newline, no second assignment, no
    /// option (rules/security.md item 4).
    #[must_use]
    pub fn as_property(&self) -> String {
        format!("CPUQuota={}%", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/cpu_quota_percent_tests.rs"]
mod tests;
