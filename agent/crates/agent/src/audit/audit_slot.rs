//! The per-request slot a handler fills and the audit layer reads.

use std::sync::{Arc, Mutex};

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::audit_outcome::AuditOutcome;
use crate::proto::{AgentError, ErrorCode};

/// What a handler has told the audit layer so far. Fixed size: a second call to
/// any setter overwrites the field, so the record can never grow.
#[derive(Default)]
struct AuditRecord {
    /// The settled outcome; `None` until the handler settles.
    outcome: Option<AuditOutcome>,
    /// The account the rpc acts on, once validated.
    account: Option<AccountName>,
    /// The site the rpc acts on, once validated.
    site: Option<Domain>,
}

/// The slot one mutating request carries to its handler through the request
/// extensions. The only way to put anything in it is a validated type or a
/// closed enum: there is no method taking a string, a path, a message or a secret,
/// so a free-text value cannot reach the audit line (threat note, sections 2 and 3).
///
/// A poisoned mutex degrades silently: every method does nothing and the line reads
/// `unset`. Nothing here ever panics, and the data of a panicked writer is never used.
#[derive(Clone)]
pub struct AuditSlot {
    /// The shared record; one per request, never shared between requests.
    record: Arc<Mutex<AuditRecord>>,
}

impl AuditSlot {
    /// A slot nothing reads: what a handler gets when the layer is absent.
    fn detached() -> Self {
        Self {
            record: Arc::new(Mutex::new(AuditRecord::default())),
        }
    }

    /// The slot the audit layer installed in `extensions`, or a detached one that
    /// absorbs every call when it did not (a service built directly by a test, or a
    /// request the layer does not audit). Handlers therefore never branch on presence.
    #[must_use]
    pub fn of(extensions: &http::Extensions) -> Self {
        extensions
            .get::<Self>()
            .cloned()
            .unwrap_or_else(Self::detached)
    }

    /// Installs a fresh slot into `extensions` and returns it. Called by the layer,
    /// once per audited request.
    pub(crate) fn install(extensions: &mut http::Extensions) -> Self {
        let slot = Self::detached();
        extensions.insert(slot.clone());
        slot
    }

    /// Records the account the rpc acts on. Takes the validated type only.
    pub fn account(&self, account: &AccountName) {
        self.write(|record| record.account = Some(account.clone()));
    }

    /// Records the site the rpc acts on. Takes the validated type only.
    pub fn site(&self, domain: &Domain) {
        self.write(|record| record.site = Some(domain.clone()));
    }

    /// Settles the rpc as a success.
    pub fn ok(&self) {
        self.write(|record| record.outcome = Some(AuditOutcome::Ok));
    }

    /// Settles the rpc as a typed refusal with the proto's own error code.
    pub fn refused(&self, code: ErrorCode) {
        self.write(|record| record.outcome = Some(AuditOutcome::Refused(code)));
    }

    /// Settles the rpc from the result a handler is about to turn into its response:
    /// `Ok` is a success, `Err` is a refusal carrying the error's code (a number the
    /// contract does not know is recorded as unspecified, never printed).
    pub fn settle<T>(&self, result: &Result<T, AgentError>) {
        match result {
            Ok(_) => self.ok(),
            Err(error) => {
                self.refused(ErrorCode::try_from(error.code).unwrap_or(ErrorCode::Unspecified));
            }
        }
    }

    /// The outcome a handler settled, if it did.
    pub(crate) fn settled_outcome(&self) -> Option<AuditOutcome> {
        self.read(|record| record.outcome).flatten()
    }

    /// The account a handler recorded, if it did.
    pub(crate) fn recorded_account(&self) -> Option<AccountName> {
        self.read(|record| record.account.clone()).flatten()
    }

    /// The site a handler recorded, if it did.
    pub(crate) fn recorded_site(&self) -> Option<Domain> {
        self.read(|record| record.site.clone()).flatten()
    }

    /// Runs `change` on the record; a poisoned mutex is skipped, not recovered.
    /// The lock is held for one field assignment, never across an await.
    fn write(&self, change: impl FnOnce(&mut AuditRecord)) {
        if let Ok(mut record) = self.record.lock() {
            change(&mut record);
        }
    }

    /// Reads from the record; `None` when the mutex is poisoned.
    fn read<R>(&self, read: impl FnOnce(&AuditRecord) -> R) -> Option<R> {
        self.record.lock().ok().map(|record| read(&record))
    }
}

#[cfg(test)]
#[path = "../tests/audit/audit_slot_tests.rs"]
mod tests;
