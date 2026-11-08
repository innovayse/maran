//! A bounded cursor over bytes a dropped child produced.

use super::entry_record_error::EntryRecordError;

/// Reads little-endian integers and byte runs from a slice, checking every
/// length against what remains BEFORE taking it. The child's bytes are hostile
/// input to a root process: no length in them is trusted.
#[derive(Debug)]
pub struct RecordReader<'a> {
    /// What has not been read yet.
    remaining: &'a [u8],
}

impl<'a> RecordReader<'a> {
    /// Starts reading at the beginning of `bytes`.
    #[must_use]
    pub fn new(bytes: &'a [u8]) -> Self {
        Self { remaining: bytes }
    }

    /// How many bytes are left.
    #[must_use]
    pub fn remaining(&self) -> usize {
        self.remaining.len()
    }

    /// Takes the next `count` bytes.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] when fewer than `count` remain.
    pub fn take(&mut self, count: usize) -> Result<&'a [u8], EntryRecordError> {
        if self.remaining.len() < count {
            return Err(EntryRecordError::Truncated);
        }
        let (taken, rest) = self.remaining.split_at(count);
        self.remaining = rest;
        Ok(taken)
    }

    /// Takes one byte.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] at the end of the bytes.
    pub fn u8(&mut self) -> Result<u8, EntryRecordError> {
        Ok(self.array::<1>()?[0])
    }

    /// Takes a little-endian `u16`.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] when two bytes do not remain.
    pub fn u16(&mut self) -> Result<u16, EntryRecordError> {
        Ok(u16::from_le_bytes(self.array()?))
    }

    /// Takes a little-endian `u32`.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] when four bytes do not remain.
    pub fn u32(&mut self) -> Result<u32, EntryRecordError> {
        Ok(u32::from_le_bytes(self.array()?))
    }

    /// Takes a little-endian `u64`.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] when eight bytes do not remain.
    pub fn u64(&mut self) -> Result<u64, EntryRecordError> {
        Ok(u64::from_le_bytes(self.array()?))
    }

    /// Takes a little-endian `i64`.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::Truncated`] when eight bytes do not remain.
    pub fn i64(&mut self) -> Result<i64, EntryRecordError> {
        Ok(i64::from_le_bytes(self.array()?))
    }

    /// Requires that every byte has been read.
    ///
    /// # Errors
    ///
    /// [`EntryRecordError::TrailingBytes`] when any remain.
    pub fn finish(self) -> Result<(), EntryRecordError> {
        if self.remaining.is_empty() {
            Ok(())
        } else {
            Err(EntryRecordError::TrailingBytes)
        }
    }

    /// Takes exactly `N` bytes as an array.
    fn array<const N: usize>(&mut self) -> Result<[u8; N], EntryRecordError> {
        let mut out = [0_u8; N];
        out.copy_from_slice(self.take(N)?);
        Ok(out)
    }
}
