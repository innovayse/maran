//! A byte string as lowercase hexadecimal.

/// Renders `bytes` as lowercase hexadecimal, two characters per byte.
///
/// The spelling every digest and fingerprint of a package is compared in.
#[must_use]
pub fn lowercase_hex(bytes: &[u8]) -> String {
    let mut rendered = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        rendered.push_str(&format!("{byte:02x}"));
    }
    rendered
}
