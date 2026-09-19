//! What the panel found when it checked its own `Match Group` block in
//! `sshd_config`.

/// The exact marker comment the installer's `installer/lib/86-sftp.sh` writes
/// immediately before the block, character for character
/// (`render_sshd_block`, `installer/lib/86-sftp.sh:122`).
///
/// The marker is what makes this a check for THIS block and not for "some
/// `Match Group` exists": the installer already uses it as the whole
/// idempotency mechanism of a re-run, deleting and rewriting everything
/// between the two markers, so re-using it here means this area is looking
/// for exactly the text the installer promises to keep current rather than
/// for a shape this area guessed at independently.
const BEGIN_MARKER: &str =
    "# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers";

/// The exact marker comment immediately after the block
/// (`installer/lib/86-sftp.sh:123`).
const END_MARKER: &str = "# END Maran SFTP";

/// The directive lines the installer writes inside the block, each as
/// `(keyword, value)`, in the order `render_sshd_block` writes them
/// (`installer/lib/86-sftp.sh:124-129`). `Match Group <group>` is not in this
/// list: its value is the group name, which the caller supplies, so it is
/// checked on its own rather than folded into a list of fixed pairs.
const REQUIRED_DIRECTIVES: [(&str, &str); 4] = [
    ("chrootdirectory", "%h"),
    ("forcecommand", "internal-sftp"),
    ("allowtcpforwarding", "no"),
    ("x11forwarding", "no"),
];

/// The keyword of the block's own opening line.
const MATCH_KEYWORD: &str = "match";

/// The condition word naming a group, as OpenSSH's `Match` criteria spell it.
const GROUP_CRITERION: &str = "group";

/// What checking the block against the live `sshd_config` found.
///
/// Two values, not a `bool`: a caller that only learns "intact or not" cannot
/// tell an operator WHAT changed, and the whole point of noticing drift is
/// being able to say what to put back.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SftpJailStatus {
    /// The installer's marker-delimited block is present, and every directive
    /// it is supposed to carry is still there with its original value.
    ///
    /// This is a claim about the FILE this area read, not about what sshd
    /// enforces for a real connection — see the caveat on
    /// [`crate::monitor::MonitorHost::read_sshd_config`].
    Intact,

    /// The block is missing, or present but missing one or more of its
    /// directives.
    ///
    /// `missing` names each absent thing in the installer's own words — the
    /// literal marker text or `keyword value` pairs — so an operator reading
    /// it can compare it directly against `installer/lib/86-sftp.sh` rather
    /// than against a paraphrase.
    Drifted {
        /// What the installer's block promises that this reading did not find.
        missing: Vec<String>,
    },
}

impl SftpJailStatus {
    /// Checks `config` for `group`'s intact block.
    ///
    /// A pure function over text, like [`super::unit_report::UnitReport::parse`]:
    /// the whole question here — did the exact block the installer writes
    /// survive — is answered by string matching, and keeping it free of any
    /// host access is what lets a test decide the input directly instead of
    /// writing a real file.
    ///
    /// # What this cannot see
    ///
    /// A block that reads back unchanged but is followed, later in this same
    /// file, by another `Match` block that also matches the sftp group and
    /// relaxes one of these directives again — OpenSSH applies the LAST
    /// matching block's directives, so a second block after this one can
    /// silently undo it. This function reports [`Self::Intact`] in that case,
    /// because the text it was told to look for is there; it is not asked to
    /// re-implement OpenSSH's own conflict resolution across the rest of the
    /// file, and doing so would still not see a change reaching sshd through
    /// an `Include`d drop-in — see the caveat on
    /// [`crate::monitor::MonitorHost::read_sshd_config`] for why that is a
    /// gap this area accepts rather than one it silently pretends to close.
    #[must_use]
    pub fn evaluate(config: &str, group: &str) -> Self {
        let mut missing = Vec::new();

        let Some(block) = extract_block(config) else {
            missing.push(format!(
                "the block between {BEGIN_MARKER:?} and {END_MARKER:?}"
            ));
            return Self::Drifted { missing };
        };

        let lines: Vec<&str> = block.lines().map(str::trim).collect();

        if !lines.iter().any(|line| matches_group(line, group)) {
            missing.push(format!("Match Group {group}"));
        }

        for (keyword, expected_value) in REQUIRED_DIRECTIVES {
            if !lines
                .iter()
                .any(|line| matches_directive(line, keyword, expected_value))
            {
                missing.push(format!("{keyword} {expected_value}"));
            }
        }

        if missing.is_empty() {
            Self::Intact
        } else {
            Self::Drifted { missing }
        }
    }
}

/// Returns the text strictly between the two markers, or `None` when either is
/// absent or they appear in the wrong order.
///
/// Only the FIRST occurrence of each marker is honoured. Two blocks would mean
/// the installer's own idempotent rewrite (which deletes every existing block
/// before writing one) did not run as designed, which is a finding about the
/// installer and not something this area silently picks a favourite among.
fn extract_block(config: &str) -> Option<&str> {
    let begin = config.find(BEGIN_MARKER)?;
    let after_begin = begin + BEGIN_MARKER.len();
    let end = config[after_begin..].find(END_MARKER)?;

    Some(&config[after_begin..after_begin + end])
}

/// Whether `line` is the block's own `Match Group <group>` opening, comparing
/// the keyword and the criterion word case-insensitively — OpenSSH itself
/// reads both without regard to case — and the group name exactly, because a
/// group name is a system identifier this product controls and any
/// case difference there is not a formatting variation this check should
/// forgive.
fn matches_group(line: &str, group: &str) -> bool {
    let mut words = line.split_whitespace();
    let Some(match_word) = words.next() else {
        return false;
    };
    let Some(criterion) = words.next() else {
        return false;
    };
    let Some(named_group) = words.next() else {
        return false;
    };

    match_word.eq_ignore_ascii_case(MATCH_KEYWORD)
        && criterion.eq_ignore_ascii_case(GROUP_CRITERION)
        && named_group == group
        && words.next().is_none()
}

/// Whether `line` is exactly `keyword value`, keyword compared
/// case-insensitively (OpenSSH's own rule) and value compared exactly.
fn matches_directive(line: &str, keyword: &str, expected_value: &str) -> bool {
    let mut words = line.split_whitespace();
    let Some(found_keyword) = words.next() else {
        return false;
    };
    let Some(found_value) = words.next() else {
        return false;
    };

    found_keyword.eq_ignore_ascii_case(keyword)
        && found_value == expected_value
        && words.next().is_none()
}

#[cfg(test)]
#[path = "../../tests/monitor/sftp_jail_status_tests.rs"]
mod tests;
