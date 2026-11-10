//! Attributing databases to the accounts whose prefix their names carry.

use std::collections::HashMap;

use maran_agent_core::validation::db::database_name::DatabaseName;

use super::model::database_assignment::DatabaseAssignment;
use super::model::listed_database::ListedDatabase;
use super::model::passwd_entry::PasswdEntry;
use super::model::source_database_record::SourceDatabaseRecord;

/// Gives each listed database to the account that owns it, through
/// [`DatabaseName::decode`], the same validator that decides what a database of
/// that account may be called on this host.
///
/// A database belongs to account `acme` when its name is `acme_` followed by
/// lowercase letters and digits only, which is what `DatabaseName::for_account`
/// can produce. The owner is looked up from the text before the LAST `_`, so a
/// name such as `acme_wp` is attributed and `acme_my_shop` (owner `acme_my`) goes
/// to an account of that name or to nobody. A database that decodes for no
/// account — a bare name, an uppercase letter, a prefix no account has — is
/// counted in `refused` and never renamed. Grants are not read in this phase, so
/// ownership is by prefix alone.
///
/// The result has one entry per account in `per_account`, in the order of
/// `accounts`.
#[must_use]
pub fn assign_databases_to_accounts(
    listed: Vec<ListedDatabase>,
    accounts: &[&PasswdEntry],
) -> DatabaseAssignment {
    let mut owners: HashMap<&str, usize> = HashMap::new();
    for (index, account) in accounts.iter().enumerate() {
        owners.entry(account.name.as_str()).or_insert(index);
    }

    let mut per_account: Vec<Vec<SourceDatabaseRecord>> = vec![Vec::new(); accounts.len()];
    let mut refused = 0;
    for database in listed {
        let attributed = database
            .name
            .rsplit_once('_')
            .and_then(|(owner, _)| owners.get(owner).copied())
            .and_then(|index| {
                let account = accounts.get(index)?;
                let name = DatabaseName::decode(&account.name, &database.name)?;
                Some((index, name))
            });
        match attributed {
            Some((index, name)) => {
                if let Some(databases) = per_account.get_mut(index) {
                    databases.push(SourceDatabaseRecord {
                        name,
                        size_bytes: database.size_bytes,
                    });
                }
            }
            None => refused += 1,
        }
    }
    DatabaseAssignment {
        per_account,
        refused,
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/assign_databases_to_accounts_tests.rs"]
mod tests;
