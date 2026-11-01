//! The most processes and threads an account's slice may hold at once.

use super::tasks_max_error::TasksMaxError;

/// The largest ceiling accepted: the kernel's own `PID_MAX_LIMIT` on 64-bit hosts, past which no
/// process could be started anyway.
pub const MAXIMUM: u32 = 4_194_304;

/// The most processes and threads an account's slice may hold at once.
///
/// It becomes systemd's `TasksMax=` on the account's `user-<uid>.slice`, enforced
/// by the cgroup v2 `pids.max` controller: a fork past it fails with `EAGAIN`
/// inside the account, so a fork bomb exhausts the account's ceiling and not
/// the host's process table.
///
/// The inner value is private and [`TasksMax::parse`] is the only constructor, so
/// holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" is a separate, typed answer the caller states on purpose (the
/// accounts area's `ResourceCeiling::Unlimited`), never a number.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct TasksMax(u32);

impl TasksMax {
    /// Validates `candidate` as a process ceiling and wraps it.
    ///
    /// # Errors
    ///
    /// - [`TasksMaxError::Zero`] when `candidate` is `0`, which is what an absent
    ///   field decodes to and which would otherwise read as a ceiling of nothing.
    /// - [`TasksMaxError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, TasksMaxError> {
        if candidate == 0 {
            return Err(TasksMaxError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(TasksMaxError::TooLarge {
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

    /// The `TasksMax=` assignment `systemctl set-property` is handed for this
    /// figure.
    ///
    /// Built from a validated integer and a fixed key, so the text can carry
    /// nothing but digits after the `=`: no newline, no second assignment, no
    /// option (rules/security.md item 4).
    #[must_use]
    pub fn as_property(&self) -> String {
        format!("TasksMax={}", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/tasks_max_tests.rs"]
mod tests;
