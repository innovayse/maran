//! What an account's shadow password field actually holds.

/// The four states a login's shadow password field can be in, as far as
/// anything this agent does is concerned.
///
/// # Why this exists at all
///
/// Because `passwd -S` cannot tell two of them apart, and the difference
/// decides whether a suspension can be reversed. Measured on both polygon
/// families, on a login the agent created and then locked:
///
/// ```text
///                       shadow field   passwd -S (Debian / RHEL)
/// never had a password   !   or  !!     L  /  LK
/// locked over a password !<hash>        L  /  LK
/// ```
///
/// The two rows are indistinguishable through `passwd -S` and completely
/// distinct in the field itself. That matters because `usermod --unlock`
/// REFUSES the first row — "unlocking the user's password would result in a
/// passwordless account" — and the refusal is spelled differently by family:
/// **exit 0 on Debian, exit 1 on RHEL**, with the same message. Every hosting
/// account is in the first row (the agent never sets a password on an
/// account's own entry), so an `unsuspend` that treats a non-zero status as a
/// failure could not succeed on any RHEL host, for any account.
///
/// The three cheap repairs are all wrong and are named here so nobody
/// re-proposes one: matching the message is locale-dependent; treating exit 1
/// as success swallows every real failure of the same call; and `passwd -S`
/// cannot make the distinction at all. Reading the field is what actually
/// answers the question, on both families, with no natural-language text
/// involved.
///
/// # What is never carried
///
/// A hash. The field is classified into this enum where it is read and the
/// bytes are dropped: no variant holds it, and nothing derived from this type
/// can print it (rules/security.md item 8).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StoredPassword {
    /// The field is empty: the login authenticates with NO password at all.
    ///
    /// Kept distinct from [`StoredPassword::Absent`] because the two are
    /// opposite facts wearing similar words — this one is an open door, and it
    /// is the state `passwd -u -f` leaves behind on the RHEL family, which is
    /// why that command is not used here.
    Empty,

    /// The field holds only lock markers (`!`, `*`) and no hash.
    ///
    /// Nothing can authenticate against it and there is nothing to restore.
    /// This is how every account the agent creates begins life — `useradd`
    /// writes `!` on the Debian family and `!!` on the RHEL one — and
    /// `usermod --lock` on such an account is a measured no-op on both, so a
    /// suspension neither changed it nor left an unsuspend anything to undo.
    Absent,

    /// A real password hash with a leading `!`: locked, and reversible.
    ///
    /// The only state in which `usermod --unlock` has work to do, and the only
    /// one in which this agent runs it.
    Locked,

    /// A real password hash with no lock marker: the login can authenticate.
    Usable,
}

impl StoredPassword {
    /// The marker characters a shadow field uses to say "no usable hash here".
    ///
    /// `!` is the lock marker written by `usermod --lock` and by `useradd`;
    /// `*` is the "this login has no password and never will" marker used for
    /// system accounts. A field made only of these carries no hash whatever
    /// their number or order, which is what makes `!` and `!!` — the two
    /// families' spellings of a fresh account — the same fact.
    const MARKERS: [char; 2] = ['!', '*'];

    /// Classifies the second field of a shadow entry.
    ///
    /// Deliberately total and deliberately conservative at the boundary: a
    /// field this function does not recognise as a marker set is treated as a
    /// hash, so an unfamiliar format is read as "there is a password here"
    /// rather than as "there is nothing to lose". Erring the other way would
    /// let an unrecognised field be treated as nothing worth unlocking.
    #[must_use]
    pub fn classify(field: &str) -> Self {
        if field.is_empty() {
            return Self::Empty;
        }
        if field
            .chars()
            .all(|character| Self::MARKERS.contains(&character))
        {
            return Self::Absent;
        }
        if field.starts_with('!') {
            return Self::Locked;
        }
        Self::Usable
    }

    /// Whether a password could authenticate this login as it stands.
    ///
    /// [`StoredPassword::Empty`] answers `true`: an empty field is a login
    /// that authenticates with the empty password, which is the one state a
    /// suspension must never leave behind.
    #[must_use]
    pub fn can_authenticate(self) -> bool {
        matches!(self, Self::Empty | Self::Usable)
    }
}

#[cfg(test)]
#[path = "../../tests/accounts/model/stored_password_tests.rs"]
mod tests;
