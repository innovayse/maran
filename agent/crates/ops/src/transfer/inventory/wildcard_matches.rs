//! Matching a file name against an `include` pattern.

/// Whether `text` matches `pattern`, where `*` matches any run of bytes
/// (including none) and `?` any single byte. Everything else matches itself.
///
/// Bounded against a hostile pattern: one longer than 256 bytes or with more
/// than two `*` matches nothing, and the scan keeps one backtrack point, so it
/// cannot grow past `pattern * text`.
#[must_use]
pub fn wildcard_matches(pattern: &str, text: &str) -> bool {
    let pattern = pattern.as_bytes();
    let text = text.as_bytes();
    if pattern.len() > 256 || pattern.iter().filter(|b| **b == b'*').count() > 2 {
        return false;
    }

    let (mut pattern_at, mut text_at) = (0, 0);
    let mut star: Option<usize> = None;
    let mut resume = 0;
    while text_at < text.len() {
        let current = pattern.get(pattern_at).copied();
        if current == Some(b'*') {
            star = Some(pattern_at);
            resume = text_at;
            pattern_at += 1;
        } else if current
            .is_some_and(|byte| byte == b'?' || Some(byte) == text.get(text_at).copied())
        {
            pattern_at += 1;
            text_at += 1;
        } else if let Some(star_at) = star {
            pattern_at = star_at + 1;
            resume += 1;
            text_at = resume;
        } else {
            return false;
        }
    }
    while pattern.get(pattern_at) == Some(&b'*') {
        pattern_at += 1;
    }
    pattern_at == pattern.len()
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/wildcard_matches_tests.rs"]
mod tests;
