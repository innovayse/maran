//! How many outside deliveries one recipient address turns a single message into.

use super::alias_fanout_error::AliasFanoutError;
use super::fanout_recipient::FanoutRecipient;

/// The most deliveries Postfix makes for one recipient: its
/// `virtual_alias_expansion_limit` (the rendered `main.cf` and this figure are one
/// number, see `MailSettings::ALIAS_EXPANSION_LIMIT`). A count above it is not a
/// bigger fan-out, because Postfix stops expanding there.
pub const MAXIMUM_FANOUT: usize = 100;

/// A recipient address and the number of outside deliveries one message to it makes.
///
/// The filter charges the sender's hourly limit that many times; this is the figure
/// it reads. The panel resolves the WHOLE expansion (an alias of aliases, a
/// catch-all) and sends the total, so the figure is what leaves the host and not
/// what the first alias lists. Built only by [`AliasFanout::parse`]; the fields are
/// public for the renderer to read.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AliasFanout {
    /// The alias, or the domain's catch-all, lower-cased.
    pub recipient: FanoutRecipient,
    /// How many outside deliveries one message makes, `0..=MAXIMUM_FANOUT`. Zero
    /// is an alias whose whole expansion stays inside the account: it is listed so
    /// the filter knows the address IS an alias (and does not fall through to the
    /// domain's catch-all), and it is charged the one delivery a mailbox costs.
    pub external_targets: u32,
}

impl AliasFanout {
    /// Validates the recipient and the count.
    ///
    /// # Errors
    ///
    /// - [`AliasFanoutError::Recipient`] and [`AliasFanoutError::CatchAllDomain`]
    ///   for a refused address.
    /// - [`AliasFanoutError::ExternalTargetsOutOfRange`] for more than
    ///   [`MAXIMUM_FANOUT`].
    pub fn parse(recipient: &str, external_targets: u32) -> Result<Self, AliasFanoutError> {
        let recipient = FanoutRecipient::parse(recipient)?;

        let in_range = usize::try_from(external_targets)
            .is_ok_and(|count| (0..=MAXIMUM_FANOUT).contains(&count));
        if !in_range {
            return Err(AliasFanoutError::ExternalTargetsOutOfRange {
                maximum: MAXIMUM_FANOUT,
            });
        }

        Ok(Self {
            recipient,
            external_targets,
        })
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/alias_fanout_tests.rs"]
mod tests;
