//! `RepairDatabaseGrants`: rewriting the pattern grants older agents issued.

use std::collections::BTreeMap;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::grant_pattern::{
    GRANT_SINGLE_CHARACTER_WILDCARD, grant_pattern_for, unescaped_grant_pattern,
};
use crate::db::list_databases::server_databases;
use crate::db::model::grant_repair_refusal::GrantRepairRefusal;
use crate::db::model::grant_repair_report::GrantRepairReport;
use crate::db::model::refused_grant::RefusedGrant;
use crate::db::model::repaired_grant::RepairedGrant;

/// The only host this panel has ever granted a database user from.
///
/// The same `localhost` `create_database` grants and `drop_database` revokes.
/// Spelled here rather than shared with them because it is read for a different
/// purpose — deciding whether a row could be ours at all — and a constant two
/// operations agree on by coincidence is not the same fact.
const USER_HOST: &str = "localhost";

/// The statement that asks the server for every database-level grant it holds.
///
/// Every row, not only the local ones: a row at another host carrying the same
/// unescaped wildcard is the same class of exposure reachable from further away,
/// and an operation that filtered it out in SQL could not report it.
const GRANT_ROWS: &str = "SELECT Host, Db, User FROM mysql.db";

/// The character the client's batch output escapes with.
const BATCH_ESCAPE: char = '\\';

/// How `SHOW GRANTS` renders the privileges `create_database` grants.
///
/// Both server families render the complete database-level privilege set as the
/// words `ALL PRIVILEGES`, and any narrower set as an explicit list, so this is
/// the whole of the privilege-shape check.
const ALL_PRIVILEGES: &str = "GRANT ALL PRIVILEGES ON ";

/// Repairs every grant on this server that this panel issued as a wildcard
/// pattern, leaving everything else alone.
///
/// # The defect this exists for
///
/// A database-level `GRANT`'s database name is a LIKE-style pattern. Until
/// `grant_pattern_for` was written, this agent issued the name unescaped, so a
/// grant on `h_stco_main` was stored as the pattern `h?stco?main` and matched
/// account `hostco`'s `hostco_main` character for character — a cross-tenant
/// read AND write, measured against a real MariaDB
/// (`docs/superpowers/notes/2026-09-13-grant-pattern-threat-note.md`). That fix
/// is forward-only: a row already in `mysql.db` keeps being matched at every
/// connection. This is the rewrite of those rows.
///
/// # The predicate: what makes a row need repair
///
/// A row is repaired only when ALL of these hold, and it is refused — never
/// guessed at — when any of them does not:
///
/// 1. its `Host` is `localhost`, the only host this panel grants from;
/// 2. its `Db` holds at least one separator and NO backslash, so it is stored as
///    a pattern rather than in the escaped form this agent now writes;
/// 3. `Db` and `User` both decode as names this agent could itself have created
///    — at the LAST separator, against the WHOLE account name — and both decode
///    to the SAME account, which is the only pairing `create_database` produces;
/// 4. `SHOW GRANTS` renders the row as exactly the `ALL PRIVILEGES` grant
///    `create_database` issues, so the re-grant restores what was there instead
///    of widening a narrower grant somebody made by hand.
///
/// **It cannot mistake an already-correct row for a broken one**, and that is
/// structural rather than careful: a repaired row's `Db` holds `\_` for every
/// separator, and condition 2 excludes any name holding a backslash. The escaped
/// form comes from `grant_pattern_for`, the same function `create_database`
/// writes with, so the two cannot drift. A clean install reports every row as
/// already correct and sends no DDL at all; so does a second run.
///
/// # Order: grant first, then revoke
///
/// The escaped pattern and the unescaped one are DIFFERENT `Db` values, so they
/// are two different rows and the server holds both between the two statements.
/// The grant is issued first deliberately:
///
/// - **grant then revoke**: a crash in between leaves the correct row PLUS the
///   old wide one. The customer keeps their access, the exposure is not yet
///   closed, and the next run sees the unescaped row and finishes — the midpoint
///   is re-validated by re-running, which the config-swap window on this branch
///   cannot claim.
/// - revoke then grant: a crash in between leaves a customer's application with
///   NO access to its own database, permanently, until an operator notices. That
///   is an outage per customer, worse than the defect being repaired.
///
/// # What is deliberately NOT here
///
/// No lock. `ops::db` takes none, and the concurrent case converges: a
/// `CreateDatabase` racing this pass writes an already-escaped row this pass does
/// not touch, and a row created after the table was read is seen by the next run.
/// Nothing here is a read-modify-write of a value this process computed — the
/// repair only narrows a pattern to the literal name it always meant.
///
/// Nor does it require the database to still exist: a row whose database an
/// operator dropped by hand is a live pattern that will match a future name.
///
/// # Idempotency
///
/// Repeating it converges: the second pass finds the escaped rows and reports
/// them as already correct. A failure part-way leaves the rows before it
/// repaired and the rest untouched, which a retry completes. The report of what
/// was done up to that point is lost with the error and re-derived by the next
/// run.
///
/// # Errors
///
/// - [`DbError::AccessDenied`] when the server refuses the agent's connection.
/// - [`DbError::Unparsable`] when the grant table's output is longer than the
///   agent will read, or a row is not the three fields it asked for.
/// - [`DbError::ClientFailed`] for any other refusal by the server, on the first
///   statement refused.
pub fn repair_grants(host: &dyn DbHost, report_only: bool) -> Result<GrantRepairReport, DbError> {
    let rows = grant_rows(host)?;
    let databases = server_databases(host)?;
    let mut rendered_grants: BTreeMap<String, Vec<String>> = BTreeMap::new();
    let mut report = GrantRepairReport {
        examined: rows.len(),
        ..GrantRepairReport::default()
    };

    for row in &rows {
        let Some((database, user)) = panel_issued_pair(row) else {
            note(&mut report, row);
            continue;
        };

        let grants = match rendered_grants.get(user.as_str()) {
            Some(lines) => lines,
            None => {
                let lines = rendered_grants_for(host, &user)?;
                rendered_grants
                    .entry(user.as_str().to_owned())
                    .or_insert(lines)
            }
        };
        if !grants_exactly_all_privileges(grants, database.as_str(), user.as_str()) {
            refuse(&mut report, row, GrantRepairRefusal::UnrecognisedPrivileges);
            continue;
        }

        let repaired = RepairedGrant {
            also_matched: also_matched_by(database.as_str(), &databases),
            database,
            user,
        };
        if report_only {
            report.would_repair.push(repaired);
            continue;
        }

        rewrite(host, &repaired)?;
        report.repaired.push(repaired);
    }

    Ok(report)
}

/// Issues the escaped grant and then revokes the unescaped one.
///
/// # Errors
///
/// Returns whatever the client failed with; see [`DbHost::execute`].
fn rewrite(host: &dyn DbHost, grant: &RepairedGrant) -> Result<(), DbError> {
    let user = grant.user.as_str();

    // Byte for byte the statement `create_database` issues today. A repaired row
    // is therefore indistinguishable from a freshly created one, which is what
    // keeps the predicate above true for both.
    host.execute(&format!(
        "GRANT ALL PRIVILEGES ON `{}`.* TO '{user}'@'{USER_HOST}'",
        grant_pattern_for(grant.database.as_str())
    ))?;

    // And only then the wide one. The server matches the `Db` column literally
    // here, so this removes the pattern row and leaves the escaped row just
    // written.
    host.execute(&format!(
        "REVOKE ALL PRIVILEGES ON `{}`.* FROM '{user}'@'{USER_HOST}'",
        grant.database.as_str()
    ))?;

    Ok(())
}

/// Every row of the server's database-level grant table.
///
/// # Errors
///
/// - [`DbError::Unparsable`] when a row is not the three fields asked for.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
fn grant_rows(host: &dyn DbHost) -> Result<Vec<[String; 3]>, DbError> {
    host.execute(GRANT_ROWS)?
        .lines()
        .filter(|line| !line.trim().is_empty())
        .map(|line| {
            let mut fields = line.split('\t').map(undo_batch_escaping);
            match (fields.next(), fields.next(), fields.next()) {
                (Some(host), Some(database), Some(user)) => Ok([host, database, user]),
                _ => Err(DbError::Unparsable),
            }
        })
        .collect()
}

/// Undoes the escaping the client applies to a field in batch output.
///
/// It matters for exactly one character and would be invisible without it: a
/// `Db` stored as `alice\_shop` is PRINTED as `alice\\_shop`, so a reader that
/// took the output literally would see a name escaped twice, refuse it as
/// unfamiliar, and report every already-correct row as one it cannot classify.
fn undo_batch_escaping(field: &str) -> String {
    let mut undone = String::with_capacity(field.len());
    let mut characters = field.chars();
    while let Some(character) = characters.next() {
        if character != BATCH_ESCAPE {
            undone.push(character);
            continue;
        }

        match characters.next() {
            Some('n') => undone.push('\n'),
            Some('t') => undone.push('\t'),
            Some('0') => undone.push('\0'),
            Some(escaped) => undone.push(escaped),
            None => undone.push(BATCH_ESCAPE),
        }
    }

    undone
}

/// The validated pair a row names, when the row is one this panel could have
/// issued as an unescaped pattern.
///
/// `None` covers three different things, and the caller tells them apart by
/// asking [`refusal_for`]: a row that is nothing to do with this panel, a row
/// that already holds the escaped form, and a row this panel might have issued
/// but whose shape is not understood.
fn panel_issued_pair(row: &[String; 3]) -> Option<(DatabaseName, DbUserName)> {
    let [host, database, user] = row;
    if host != USER_HOST
        || database.contains(BATCH_ESCAPE)
        || !database.contains(GRANT_SINGLE_CHARACTER_WILDCARD)
    {
        return None;
    }

    decoded_pair(database, user)
}

/// The validated pair `database` and `user` name, when both decode to one
/// account.
///
/// The account is read off the database name at its LAST separator and the two
/// halves are then decoded by the types themselves — the inverse of the
/// constructors that built them — so a name this function returns is a name this
/// agent could itself have created. A prefix scan would be the wrong predicate
/// for the reason `list_databases` documents: `alice_` is a prefix of
/// `alice_bob_shop`, which is account `alice_bob`'s.
fn decoded_pair(database: &str, user: &str) -> Option<(DatabaseName, DbUserName)> {
    let (owner, _) = database.rsplit_once(GRANT_SINGLE_CHARACTER_WILDCARD)?;
    let account = AccountName::parse(owner).ok()?;

    Some((
        DatabaseName::decode(&account, database)?,
        DbUserName::decode(&account, user)?,
    ))
}

/// Records a row [`panel_issued_pair`] returned nothing for.
///
/// A row that could never carry a wildcard, or that already holds the escaped
/// form this agent writes, is counted as already correct and is not reported:
/// there is nothing for an operator to decide about it. Everything else is a
/// refusal, with its reason.
fn note(report: &mut GrantRepairReport, row: &[String; 3]) {
    match refusal_for(row) {
        Some(reason) => refuse(report, row, reason),
        None => report.already_correct += 1,
    }
}

/// Why a row that will not be repaired is a refusal rather than a no-op.
fn refusal_for(row: &[String; 3]) -> Option<GrantRepairRefusal> {
    let [host, database, user] = row;
    if !database.contains(GRANT_SINGLE_CHARACTER_WILDCARD) {
        // No separator, so no wildcard and nothing at stake — `mysql`,
        // `information_schema`, a hand-made `shop`. Not this panel's business
        // either way.
        return None;
    }

    if host != USER_HOST {
        return Some(GrantRepairRefusal::HostIsNotLocalhost);
    }

    if database.contains(BATCH_ESCAPE) {
        let plain = unescaped_grant_pattern(database);
        let correctly_escaped = !plain.contains(BATCH_ESCAPE)
            && grant_pattern_for(&plain) == *database
            && decoded_pair(&plain, user).is_some();

        return (!correctly_escaped).then_some(GrantRepairRefusal::PartiallyOrUnfamiliarlyEscaped);
    }

    Some(GrantRepairRefusal::NotThePanelsNaming)
}

/// Adds `row` to the report's refusals and says so in the log.
///
/// The log line exists because a refusal is the answer for something nobody
/// understands: the report reaches the panel, and an operator reading the agent's
/// own journal must be able to see that a row on this host was left alone and
/// why. Names are not secrets here — `list_databases` already returns them — and
/// nothing in a grant row is a credential.
fn refuse(report: &mut GrantRepairReport, row: &[String; 3], reason: GrantRepairRefusal) {
    let [host, database, user] = row;
    tracing::warn!(
        grant_host = host.as_str(),
        database = database.as_str(),
        user = user.as_str(),
        "grant repair refused a row it cannot classify: {reason}"
    );
    report.refused.push(RefusedGrant {
        host: host.clone(),
        database: database.clone(),
        user: user.clone(),
        reason,
    });
}

/// Every grant the server renders for `user`, one per line.
///
/// # Errors
///
/// Returns whatever the client failed with; see [`DbHost::execute`].
fn rendered_grants_for(host: &dyn DbHost, user: &DbUserName) -> Result<Vec<String>, DbError> {
    Ok(host
        .execute(&format!(
            "SHOW GRANTS FOR '{}'@'{USER_HOST}'",
            user.as_str()
        ))?
        .lines()
        .map(undo_batch_escaping)
        .collect())
}

/// Whether `grants` holds exactly the `ALL PRIVILEGES` grant this panel issues
/// on `pattern` for `user`.
///
/// The comparison is against the server's own rendering with identifier quoting
/// removed, because the families quote it differently — backticks on the current
/// ones, single quotes on older MySQL — and that is the only part that varies.
/// What must NOT vary is the privilege phrase: a narrower grant renders as an
/// explicit list and fails here, which stops the re-grant escalating it.
fn grants_exactly_all_privileges(grants: &[String], pattern: &str, user: &str) -> bool {
    let expected = format!("{ALL_PRIVILEGES}{pattern}.* TO {user}@{USER_HOST}");

    grants.iter().any(|line| unquoted(line.trim()) == expected)
}

/// `line` with every identifier quote character removed.
fn unquoted(line: &str) -> String {
    line.chars().filter(|c| *c != '`' && *c != '\'').collect()
}

/// The other databases on this server that the unescaped `pattern` also reaches.
///
/// The pattern's only metacharacter is `_`, which matches exactly one character,
/// so a match is a name of the SAME LENGTH agreeing everywhere the pattern holds
/// something else. `%` cannot occur in a name this agent created, and a name
/// containing one would have been refused by the predicate rather than reaching
/// here.
fn also_matched_by(pattern: &str, databases: &[String]) -> Vec<String> {
    databases
        .iter()
        .filter(|name| name.as_str() != pattern && matches_pattern(pattern, name))
        .cloned()
        .collect()
}

/// Whether `name` is matched by `pattern` read as the server reads it.
fn matches_pattern(pattern: &str, name: &str) -> bool {
    pattern.len() == name.len()
        && pattern
            .bytes()
            .zip(name.bytes())
            .all(|(expected, actual)| expected == actual || expected == b'_')
}

#[cfg(test)]
#[path = "../tests/db/repair_grants_tests.rs"]
mod tests;
