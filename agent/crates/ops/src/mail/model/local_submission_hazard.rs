//! The reasons local mail submission is refused before anything is written.

/// A fact of the host that would let a customer bypass the local-submission rule
/// (docs/mail/local-submission.md, issue #518).
///
/// The rule trusts the uid `pickup` stamps on a message. That is only true while
/// the maildrop road stays closed to everything but `postdrop` itself, so the
/// agent refuses to bind an account on a host where it is not.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[non_exhaustive]
pub enum LocalSubmissionHazard {
    /// The account's system user is in a group that can write the maildrop queue
    /// or reach the filter's socket directly (its primary or a supplementary group).
    #[error("the account's system user belongs to the {group} group")]
    MemberOfPrivilegedGroup {
        /// The group's name.
        group: String,
    },

    /// `postdrop` is missing, or is not mode 2755 owned by root and the drop group:
    /// a customer-writable copy or a different setgid helper opens the queue.
    #[error("{path} is not mode 2755 owned by root and the {group} group")]
    DropBinaryUnsafe {
        /// The path checked.
        path: String,
        /// The group it must belong to.
        group: String,
    },

    /// The drop group is not in the group database, so membership cannot be judged.
    #[error("the {group} group is not in the group database")]
    DropGroupUnknown {
        /// The group's name.
        group: String,
    },

    /// The account's system user cannot be found in the user database the agent
    /// reads, so whether it is in a privileged group cannot be judged. Never read
    /// as "safe": a directory-service user is invisible to the file.
    #[error("no user with uid {uid} is in the user database")]
    SubmitterUnknown {
        /// The uid that has no entry.
        uid: u32,
    },

    /// Rspamd does not report the local-submission rule's settings after the
    /// reload, so the rule is not loaded and the local road would be examined by
    /// nothing (or by the rule's refusing stand-in).
    #[error("the local-submission rule is not loaded by the spam filter")]
    RuleNotLoaded,
}
