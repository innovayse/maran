//! What the startup reconcile of the output chain found and did.

/// What `reconcile_output_chain` found and did.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OutputChainOutcome {
    /// The host has no ruleset file yet (the installer seeds it); nothing to do.
    NotSeeded,
    /// The file already carried the chain for the host's present facts and the
    /// kernel already had it loaded; nothing was written or loaded.
    Current,
    /// The file lacked the chain, or held other host facts (a new address, another
    /// uid floor); it was re-rendered and loaded.
    Rewritten,
    /// The file was current but the kernel had no output chain (the firewall was
    /// flushed or never loaded); the file was loaded.
    Loaded,
}
