//! systemd's path escaping, restricted to the paths this agent builds, in one
//! place for every jail that needs it.

/// The characters this rule leaves alone besides ASCII letters and digits.
///
/// Everything else becomes `\xNN`, and `/` becomes `-`.
///
/// **It is not systemd's own set, which is `_.:`.** `:` is escaped here and
/// kept by `systemd-escape --path` — one of the four measured differences named
/// on [`systemd_escape`], none of which the two callers can reach.
const UNESCAPED_SYMBOLS: &str = "_.";

/// Escapes an absolute jail path the way systemd escapes one into a unit name.
///
/// The leading separator is dropped, every remaining `/` becomes `-`, ASCII
/// letters, digits and [`UNESCAPED_SYMBOLS`] are kept, and every other byte
/// becomes `\xNN` in lowercase hexadecimal.
///
/// # What it is, and what it is not
///
/// It is `systemd-escape --path` **for the paths this agent's two jails build,
/// and for nothing else**. That is a measurement and not a reading of the
/// documentation: compiled out of this file's own text and run against the real
/// `systemd-escape --path` over all 78 paths the two jail types can produce
/// (both roots × thirteen `AccountName` shapes × the jail directory, its mount
/// point and the account's home), the two answers were byte-identical. The same
/// comparison found four inputs where they are NOT, and each is named here
/// rather than left for a reader to discover on a host:
///
/// ```text
/// input          this rule        systemd-escape --path
/// /              (empty)          -
/// /a:b/c         a\x3ab-c         a:b-c
/// /.hidden/x     .hidden-x        \x2ehidden-x
/// //a//b/        a--b-            a-b
/// ```
///
/// None of the four can arise here, and that is a property of the CALLERS
/// rather than a hope: both build `<root>/<account>` and `<root>/<account>/home`
/// from a literal root — `/var/lib/maran-sftp`, `/var/lib/maran-ftps` — and an
/// [`AccountName`](maran_agent_core::validation::system::name::AccountName),
/// whose alphabet is `^[a-z][a-z0-9_]{2,29}$`. So the path is absolute and
/// non-empty, its separators are single and never trailing, it carries no `:`,
/// and its first component is `var`, never a dot.
///
/// **A caller handing this a free-form path would therefore be a change of
/// contract, and would have to compare against the real tool first** — the four
/// rows above are what a reading of the documentation misses. An earlier
/// version of this comment claimed the full rule was implemented; it was not,
/// and the claim is what would have stopped the next reader from checking.
///
/// The general byte loop is kept rather than narrowed to the two substitutions
/// the paths actually need (`/` and the `-` inside both roots), and that is
/// worth one sentence because it is the opposite of the paragraph above: within
/// the stated domain the loop still agrees with systemd byte for byte if the
/// account alphabet ever widens — an uppercase letter, a dot, a space, a `+`
/// and a multi-byte character were all measured to agree — whereas a
/// two-substitution version would silently pass such a byte through and derive
/// a name systemd does not.
///
/// # Why one implementation
///
/// It lives here, under `logins`, rather than beside either jail, because there
/// are now two of them — `sftp::AccountJail` and `ftps::FtpsJail` — and they
/// must derive the same name from the same rule. A `.mount` unit whose file
/// name is not the escaping of its own `Where=` is one systemd silently refuses
/// to load, and the symptom is a login that lands in an empty jail on a real
/// host and in nothing at all in a build. Two copies of this rule would be two
/// chances for that, one of which nobody would be looking at.
///
/// It takes no lock and touches no host resource, so it adds nothing to the set
/// in rules/rust.md "What this agent serialises": it is a pure function of its
/// argument, callable from anywhere, including inside another area's critical
/// section.
pub(crate) fn systemd_escape(path: &str) -> String {
    let mut escaped = String::with_capacity(path.len());

    for byte in path.trim_start_matches('/').bytes() {
        let character = char::from(byte);
        if byte == b'/' {
            escaped.push('-');
        } else if character.is_ascii_alphanumeric() || UNESCAPED_SYMBOLS.contains(character) {
            escaped.push(character);
        } else {
            escaped.push_str(&format!("\\x{byte:02x}"));
        }
    }

    escaped
}

#[cfg(test)]
#[path = "../tests/logins/systemd_escape_tests.rs"]
mod tests;
