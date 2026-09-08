//! What an anonymous fetch of a bucket's own object was able to establish.

/// The answer to "can the internet read what we put in this bucket?" — with a
/// third state for "the question could not be asked".
///
/// # Why this is not a boolean
///
/// The check behind this type writes a canary object into the operator's bucket
/// and then fetches it back over plain HTTPS **with no credentials at all**.
/// The reason it is done that way rather than by reading the bucket's policy is
/// that a policy read reports on a document, and what matters is what the
/// internet actually gets: a policy the panel parses as private, sitting under
/// an account-level block the panel did not read, in front of a CDN that serves
/// the object anyway, is three ways of being wrong about one HTTP response.
///
/// That argument only survives if the outcome can say the check did not happen.
/// A DNS failure, a timeout, a proxy that ate the request, a 500 from the
/// provider — none of them is evidence that the object is private, and a
/// boolean has nowhere to put them. It has exactly two homes for such a result:
/// `true`, which refuses a bucket that may be perfectly fine, or `false`, which
/// **reports a bucket as private on the strength of a request that was never
/// answered**. The second is the one that gets chosen, because the first is
/// visibly annoying and the second is invisible; and it is a check that cannot
/// observe what it reports on (rules/testing.md).
///
/// So the third state exists, it carries the reason the fetch failed, and
/// [`Self::destination_is_acceptable`] answers `true` for [`Self::Private`]
/// alone. An operator whose destination is refused as
/// [`Self::Unproven`](Self::Unproven) reads why and can act on it; nobody is
/// told a bucket is safe because the panel could not reach it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum PublicReadVerdict {
    /// The anonymous fetch was made, and the destination refused it.
    ///
    /// The only state a destination may be saved in.
    Private,

    /// The anonymous fetch was made, and the object came back.
    ///
    /// The bucket serves this product's backups — every file in a customer's
    /// home and a full dump of every database they own — to anyone with the
    /// URL.
    PubliclyReadable,

    /// The anonymous fetch could not be made, or was answered by something
    /// that says nothing about the bucket's policy.
    ///
    /// **This is not [`Self::Private`], and nothing may treat it as such.**
    Unproven {
        /// Why the fetch proved nothing, in words an operator can act on.
        ///
        /// Redacted before it is built: the transport's own message can quote a
        /// signed URL, and this string reaches the panel and an operator's log
        /// (rules/security.md item 8).
        reason: String,
    },
}

impl PublicReadVerdict {
    /// Whether a destination carrying this verdict may be saved.
    ///
    /// `true` for [`Self::Private`] and for nothing else. The collapse to a
    /// boolean happens HERE, once, where the three states are still in scope
    /// and a reader can see which of them are refused — rather than at each
    /// caller, where `!= PubliclyReadable` is one plausible keystroke away and
    /// would silently accept every unproven probe.
    #[must_use]
    pub fn destination_is_acceptable(&self) -> bool {
        matches!(self, Self::Private)
    }
}

#[cfg(test)]
#[path = "../../tests/backup/public_read_verdict_tests.rs"]
mod tests;
