//! Failures of the host firewall operations.

#[cfg(doc)]
use maran_agent_core::agent_paths::AgentPaths;

/// What can go wrong while reading, changing or loading the host firewall.
///
/// One exhaustive list for the whole area (rules/rust.md "Errors"). Three of
/// the variants carry `nft`'s own standard error, and that is deliberate
/// rather than an oversight: this surface is admin-only, an `nft` refusal is
/// unintelligible without the message it came with, and the message is the
/// operator's only way to tell "port 0 is not a port" from "this kernel has no
/// `inet` family". Nothing a hosting customer sees is formatted from these
/// (rules/security.md, role-aware errors) — the panel maps the variant.
///
/// What no variant carries is a rule, an address or a path. There is nothing
/// here for a caller-supplied byte to travel in, so a refusal cannot echo back
/// a value the caller planted.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum FirewallError {
    /// The ruleset already grants exactly what was asked for.
    ///
    /// The idempotent answer to a repeated allow. It is decided against the
    /// RENDERED text as well as against the parsed rule list, because the two
    /// answer different questions: the list says "this rule is recorded", and
    /// the text says "the firewall already behaves this way". The second is
    /// what catches an allow for the SSH port from any source, which the
    /// unconditional fallback already grants without any rule being recorded
    /// (R2).
    #[error("the firewall already allows this")]
    AlreadyExists,

    /// The ruleset holds no such rule, or no such ban is in force.
    ///
    /// The idempotent answer to a repeated deny or unban.
    #[error("the firewall holds no such rule or ban")]
    NotFound,

    /// The file at the ruleset path was not written by this agent.
    ///
    /// Returned before anything is staged, and nothing is overwritten. The
    /// file is the agent's rule store, so a file it did not render is either
    /// an operator's own ruleset that happens to sit at that path or a
    /// half-written one from a crashed apply — and replacing either with a
    /// ruleset built from a rule list this agent inferred is how a host loses
    /// the rules it was actually running.
    ///
    /// A file that carries this agent's marker but whose chain preamble or
    /// rule region cannot be read back lands here too. "I wrote this and I
    /// cannot read it" and "somebody else wrote this" call for the same
    /// answer: refuse, and change nothing.
    #[error("the ruleset file was not written by this agent")]
    ForeignRuleset,

    /// The ruleset IS one this agent rendered, and it was rendered for
    /// different ports than the request declares.
    ///
    /// Its own variant rather than a [`Self::ForeignRuleset`], and the split is
    /// the difference between "somebody else wrote this file" and "this file
    /// and this request describe different hosts". It is reached only after the
    /// marker line, the replace idiom, the chain preamble and the closing
    /// braces have all matched this agent's own render — so the file is
    /// certainly ours, and only the ports disagree.
    ///
    /// Every caller sends the host's SSH ports and its panel port, and the
    /// rendered file accepts each of them unconditionally. Those accepts are
    /// byte-identical to an operator's own any-source rule, so the ports are
    /// the only way to tell a derived line from a recorded one.
    ///
    /// # It does not say whose fault it is, because it cannot tell
    ///
    /// Two things produce it and this agent cannot distinguish them: a caller
    /// that sent the wrong ports, or a rendered file that has gone stale
    /// because sshd was moved after it was written. The second is the likelier
    /// one in practice — the ports on the wire come from the installer's own
    /// detection, and the file is the older artifact — so the message names it
    /// rather than asserting the request is wrong.
    ///
    /// # This is the one state no rpc recovers from
    ///
    /// Recorded here because it is a real limit rather than an oversight, and
    /// belongs in the threat note (Task 17).
    ///
    /// Once the two disagree, every rpc that touches the RULE store refuses:
    /// `allow_port` and `deny_port` because they begin with this read, and
    /// `list_rules` for the same reason. Neither writer reaches its write —
    /// they are the only two callers of `apply_ruleset` for this path, and both
    /// read first — so no rpc re-seeds the file, and none removes it. Nothing
    /// the panel can send puts the host back into a state the panel can drive.
    ///
    /// The BAN rpcs are unaffected, which is worth knowing rather than
    /// assuming: `ban_address`, `unban_address` and `list_bans` never read this
    /// file. They work on `table inet maran_bans` through its own path, so
    /// brute-force banning keeps running while an operator fixes the ruleset —
    /// the wedge costs rule management, not ban enforcement.
    ///
    /// Recovery is therefore an operator action, and it is a path that already
    /// exists rather than one invented for this: the installer seeds the file
    /// by running `maran-agent render-firewall-ruleset` and writing the result
    /// to [`AgentPaths::nftables_ruleset_path`], and an operator re-runs
    /// exactly that with the host's current ports. That is what the message
    /// says out loud, because the alternative is an administrator at three in
    /// the morning reading "the ports disagree" and guessing.
    ///
    /// Nothing is written when this is returned: it comes from the read that
    /// every mutation begins with, so the live ruleset and the kernel are
    /// whatever they already were.
    #[error(
        "the firewall ruleset was rendered for different ports than this request declares; \
         if the host's ssh ports changed, the RULESET is the stale half — re-render it with \
         `maran-agent render-firewall-ruleset --ssh-port <port> --panel-port <port>` \
         (repeat --ssh-port once per port sshd listens on) and write it to the agent's \
         ruleset path"
    )]
    PortsDisagree,

    /// `nft --check` refused the rendered ruleset.
    ///
    /// The LIVE ruleset is untouched when this is returned: the check runs
    /// against the staged temporary file, before the rename, so a refusal
    /// happens while the real path still holds the previous content and
    /// nothing has been loaded.
    #[error("nft refused the ruleset: {stderr}")]
    RuleRefusedByNft {
        /// Everything `nft` wrote to standard error, verbatim.
        stderr: String,
    },

    /// An `nft` invocation failed for a reason this area does not name.
    ///
    /// Loading a checked file, listing a table, adding or deleting a set
    /// element — anything that exits non-zero without a named meaning, and
    /// the failure to start `nft` at all.
    #[error("an nft command failed: {stderr}")]
    NftFailed {
        /// Everything `nft` wrote to standard error, or a description of why
        /// it could not be started.
        stderr: String,
    },

    /// `nft` answered in JSON this agent could not read.
    ///
    /// Its own variant rather than a [`Self::NftFailed`], because the command
    /// SUCCEEDED and the disagreement is between this agent and the version of
    /// `nft` on the host. An operator reading it needs to look at their nft
    /// version, not at their firewall.
    #[error("nft answered with json this agent could not read")]
    UnreadableNftOutput,

    /// The ruleset file could not be read.
    ///
    /// Not "it is not there" — an absent ruleset is an empty rule set and a
    /// perfectly ordinary state on a host the installer has not seeded yet.
    /// This is a file that exists and will not be read.
    #[error("the ruleset file could not be read")]
    RulesetUnreadable,

    /// A template failed to render.
    ///
    /// Can only happen if a template and its render type have drifted apart,
    /// since every value reaching one is already validated.
    #[error("the firewall configuration could not be rendered")]
    RenderFailed,

    /// The staged file could not be written, flushed, renamed into place, or
    /// made durable afterwards.
    ///
    /// **A half-written ruleset is never left at the real path.** Every
    /// failure up to and including the rename leaves the live ruleset exactly
    /// as it was, because a rename moves the whole directory entry or does not
    /// happen at all.
    ///
    /// **One failure arrives AFTER the rename, and this variant covers it
    /// too**: the `fsync` of the directory the rename published the file in,
    /// which is what makes the rename survive a crash. An `EIO` from a failing
    /// disk reaches it, so it is genuinely reachable rather than theoretical.
    /// When it is what failed, the new ruleset IS on disk and complete, and
    /// whether the kernel is running it depends on nothing this variant can
    /// say — the load had not been attempted yet, so it is not. The same
    /// operation retried converges: it re-renders the same text, re-checks it,
    /// renames again and loads.
    ///
    /// The variant deliberately does not distinguish the two, because a
    /// caller's response is the same for both — retry the operation — and a
    /// second variant would ask the panel to model a difference it cannot act
    /// on. What must not happen is this doc claiming a guarantee the code
    /// stopped offering, which is what it did before the flush moved to the
    /// far side of the rename.
    #[error("the firewall file could not be staged")]
    StagingFailed,
}
