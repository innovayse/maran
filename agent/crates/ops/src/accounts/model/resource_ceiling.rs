//! A ceiling an account's slice either has or does not have.

/// What a plan states about one resource of an account's slice: a validated
/// ceiling, or none at all.
///
/// A type of its own rather than an `Option` or a sentinel figure, because "no
/// ceiling" is an answer the panel states on purpose and systemd spells
/// differently per property (`CPUQuota=` empty, `MemoryMax=infinity`,
/// `TasksMax=infinity`). A zero, or an absent value, standing in for it would be
/// one keystroke away from a ceiling of nothing — so the validated figures refuse
/// zero, and this enum is the only way to say "unlimited".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ResourceCeiling<T> {
    /// The plan states no ceiling for this resource.
    Unlimited,
    /// The plan caps the resource at the validated figure.
    Limited(T),
}
