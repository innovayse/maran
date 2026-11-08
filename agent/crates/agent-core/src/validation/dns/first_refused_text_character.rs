//! The one alphabet of free text that may be written between quotes in a zone file.

/// Returns the first character of `text` that may not appear inside a quoted
/// zone-file string, or `None` when every character is allowed.
///
/// Allowed: printable ASCII, `0x20` through `0x7E`, except the double quote and
/// the backslash. Everything else is refused, which is what removes the
/// newline, carriage return, NUL and other control characters that would end
/// the line, and the two characters that open or escape inside a quoted string.
/// The semicolon and the dollar sign ARE allowed: inside quotes they are data
/// (a DMARC value is `v=DMARC1; p=none`), and the zone-file parser reads nothing
/// between quotes as a comment or a directive. The rule is a closed alphabet, so
/// no list of dangerous characters has to be kept complete.
pub(crate) fn first_refused_text_character(text: &str) -> Option<char> {
    text.chars()
        .find(|c| !(' '..='~').contains(c) || *c == '"' || *c == '\\')
}
