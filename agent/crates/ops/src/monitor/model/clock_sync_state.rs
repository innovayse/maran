//! Whether the host's clock is kept in step with a time source.

/// What the kernel and the host's time-synchronisation setup say about the
/// system clock (issue #150).
///
/// **This is the kernel's own verdict, not a measured offset.** The kernel
/// keeps an estimate of how far its clock may have strayed (its maximum
/// error), and clears its "synchronised" flag once that estimate passes
/// sixteen seconds — which is what happens when no synchronisation daemon has
/// disciplined the clock for some hours. So a clock that is drifting is
/// reported as [`Self::NotSynchronized`] once the kernel itself stops vouching
/// for it. Measuring the offset against a reference would need an outbound
/// call to a time server, and this agent makes none (rules/security.md item 10).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum ClockSyncState {
    /// The kernel reports its clock synchronised.
    Synchronized,
    /// A synchronisation service is enabled, but the kernel does not report
    /// its clock synchronised: the service has not reached a time source yet,
    /// or has not for long enough that the kernel stopped vouching for the
    /// clock.
    NotSynchronized,
    /// No synchronisation service is enabled, and the kernel does not report
    /// its clock synchronised. Nothing on the host will correct a drift.
    SyncServiceDisabled,
}
