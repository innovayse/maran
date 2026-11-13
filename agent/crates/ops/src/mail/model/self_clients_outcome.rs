//! What a refresh of the table of this host's own client addresses found and did.

/// What `refresh_self_clients` found and did.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SelfClientsOutcome {
    /// Mail was never enabled on this host (no table on disk); nothing was written.
    NotEnabled,
    /// The table is there but Postfix is not running (mail was disabled, or it is down
    /// for maintenance). The table now holds the rendered bytes, written and checked
    /// without a reload when it differed, so a Postfix started next reads the current
    /// addresses. Postfix was NOT reloaded or started, which is why this is not
    /// [`Self::Rewritten`]: the layer is not yet in effect.
    NotRunning,
    /// The table already held the rendered bytes; nothing was written and Postfix
    /// was not reloaded.
    Unchanged,
    /// The table was rewritten, Postfix's own check accepted it and Postfix was
    /// reloaded.
    Rewritten,
}
