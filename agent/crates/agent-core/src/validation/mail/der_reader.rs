//! A bounds-checked reader for the few DER elements a private key is made of.

/// Reads DER elements one after another from a byte slice, refusing anything
/// malformed instead of guessing.
///
/// It exists for one purpose — proving that a PKCS#8 RSA key is the structure it
/// claims to be before a root process writes it — and reads nothing beyond
/// tag, length and content. Every read is bounds-checked and returns `None`
/// on the first irregularity, so no index into the input can panic.
///
/// It accepts only DER's minimal length forms (a long form that could have
/// been short, or one with a leading zero byte, is refused) and lengths of at
/// most three bytes, which is ample for a key and bounds what a hostile length
/// can ask for.
#[derive(Debug, Clone, Copy)]
pub(crate) struct DerReader<'a> {
    /// What has not been read yet.
    remaining: &'a [u8],
}

impl<'a> DerReader<'a> {
    /// The tag of an INTEGER.
    pub(crate) const INTEGER: u8 = 0x02;

    /// The tag of a NULL.
    pub(crate) const NULL: u8 = 0x05;

    /// The tag of an OBJECT IDENTIFIER.
    pub(crate) const OBJECT_IDENTIFIER: u8 = 0x06;

    /// The tag of an OCTET STRING.
    pub(crate) const OCTET_STRING: u8 = 0x04;

    /// The tag of a SEQUENCE.
    pub(crate) const SEQUENCE: u8 = 0x30;

    /// Starts reading at the beginning of `bytes`.
    pub(crate) fn new(bytes: &'a [u8]) -> Self {
        Self { remaining: bytes }
    }

    /// Whether every byte has been read.
    pub(crate) fn is_empty(&self) -> bool {
        self.remaining.is_empty()
    }

    /// Reads the next element, which must carry `tag`, and returns its content.
    ///
    /// Returns `None` when the next element has another tag, when the length is
    /// not minimal DER or does not fit the input, or when nothing is left. The
    /// reader does not advance on `None`.
    pub(crate) fn read(&mut self, tag: u8) -> Option<&'a [u8]> {
        let (&found, rest) = self.remaining.split_first()?;
        if found != tag {
            return None;
        }

        let (&first, rest) = rest.split_first()?;
        let (length, rest) = if first < 0x80 {
            (usize::from(first), rest)
        } else {
            let count = usize::from(first & 0x7f);
            if count == 0 || count > 3 {
                return None;
            }
            let (digits, rest) = rest.split_at_checked(count)?;
            if digits.first() == Some(&0) {
                return None;
            }
            let length = digits
                .iter()
                .fold(0usize, |acc, digit| (acc << 8) | usize::from(*digit));
            if length < 0x80 {
                return None;
            }
            (length, rest)
        };

        let (content, remainder) = rest.split_at_checked(length)?;
        self.remaining = remainder;
        Some(content)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/der_reader_tests.rs"]
mod tests;
