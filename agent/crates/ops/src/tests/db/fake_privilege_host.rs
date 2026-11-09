//! An in-memory [`DbHost`] that models logins and their `mysql.db` rows.
//!
//! Separate from `FakeDbHost` because the questions differ. That fake answers
//! "was this statement sent"; this one has to answer "what does the server hold
//! AFTER these statements", since a privilege change is judged by the row it
//! leaves behind. It keeps one row per `(user, Db)` with a set of privileges and a
//! grant-option flag, applies `GRANT` and `REVOKE` to them, and answers the read
//! the operation makes in the format the real client prints. It parses statements
//! by position and keyword, deliberately dumber than the code it judges, and
//! panics on a statement nobody expected so an operation cannot send anything and
//! still pass.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;
use maran_agent_core::validation::db::database_privilege_set::DatabasePrivilegeSet;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::model::database_user_request::DatabaseUserRequest;
use crate::db::privilege_column::{GRANT_OPTION_COLUMN, privilege_column};
use maran_agent_core::validation::system::name::AccountName;

/// One `mysql.db` row.
#[derive(Debug, Clone)]
struct Row {
    /// The user the row belongs to.
    user: String,
    /// The `Db` column exactly as stored.
    database: String,
    /// The privileges of the closed set it holds.
    privileges: DatabasePrivilegeSet,
    /// Whether it carries the grant option.
    grant_option: bool,
}

/// What the "server" holds.
#[derive(Default)]
struct Server {
    /// The databases, by name.
    databases: Vec<String>,
    /// The logins, by name (always at `localhost`).
    users: Vec<String>,
    /// The passwords, by login.
    passwords: Vec<(String, String)>,
    /// The `mysql.db` rows.
    rows: Vec<Row>,
    /// Every statement asked, in order.
    statements: Vec<String>,
}

/// The fake. See the module note.
pub(crate) struct FakePrivilegeHost {
    /// All state, behind one lock.
    server: Mutex<Server>,
    /// A refusal installed for statements starting with a prefix.
    refusal: Mutex<Option<(String, i32, String)>>,
    /// An answer printed for statements starting with a prefix, in place of the
    /// real one.
    answer: Mutex<Option<(String, String)>>,
}

impl FakePrivilegeHost {
    /// A server holding nothing.
    pub(crate) fn new() -> Self {
        Self {
            server: Mutex::new(Server::default()),
            refusal: Mutex::new(None),
            answer: Mutex::new(None),
        }
    }

    /// A server holding `alice`'s `shop` database and her `reader` login, and no
    /// row for it.
    pub(crate) fn with_reader_and_shop() -> Self {
        let host = Self::new();
        host.add_database("alice_shop");
        host.add_user("alice_reader");

        host
    }

    /// Adds a database.
    pub(crate) fn add_database(&self, name: &str) {
        self.server.lock().unwrap().databases.push(name.to_owned());
    }

    /// Adds a login with no password and no row.
    pub(crate) fn add_user(&self, name: &str) {
        self.server.lock().unwrap().users.push(name.to_owned());
    }

    /// Plants a `mysql.db` row exactly as given: `database` is the `Db` column as
    /// stored, so a test can install the escaped form, the unescaped one, or a
    /// neighbour's.
    pub(crate) fn plant_row(
        &self,
        user: &str,
        database: &str,
        privileges: &[DatabasePrivilege],
        grant_option: bool,
    ) {
        self.server.lock().unwrap().rows.push(Row {
            user: user.to_owned(),
            database: database.to_owned(),
            privileges: DatabasePrivilegeSet::of(privileges).unwrap(),
            grant_option,
        });
    }

    /// Makes every statement starting with `prefix` fail as the client would with
    /// `code` and `stderr`.
    pub(crate) fn refuse_statements_starting_with(&self, prefix: &str, code: i32, stderr: &str) {
        *self.refusal.lock().unwrap() = Some((prefix.to_owned(), code, stderr.to_owned()));
    }

    /// Makes every statement starting with `prefix` print `printed` instead of
    /// its real answer, for a test of what a shape the operation does not read
    /// does.
    pub(crate) fn print_for_statements_starting_with(&self, prefix: &str, printed: &str) {
        *self.answer.lock().unwrap() = Some((prefix.to_owned(), printed.to_owned()));
    }

    /// Every statement asked, in order.
    pub(crate) fn statements(&self) -> Vec<String> {
        self.server.lock().unwrap().statements.clone()
    }

    /// Every statement asked that changes something (not a read).
    pub(crate) fn changing_statements(&self) -> Vec<String> {
        self.statements()
            .into_iter()
            .filter(|statement| !statement.starts_with("SELECT ") && statement != "SHOW DATABASES")
            .collect()
    }

    /// The logins held now.
    pub(crate) fn users(&self) -> Vec<String> {
        self.server.lock().unwrap().users.clone()
    }

    /// The password held for `user`.
    pub(crate) fn password_of(&self, user: &str) -> Option<String> {
        self.server
            .lock()
            .unwrap()
            .passwords
            .iter()
            .find(|(held, _)| held == user)
            .map(|(_, password)| password.clone())
    }

    /// The privileges `user` holds in the row stored under `database`, if any.
    pub(crate) fn privileges_of(&self, user: &str, database: &str) -> Option<DatabasePrivilegeSet> {
        self.row_of(user, database).map(|row| row.0)
    }

    /// Whether the row stored under `database` for `user` carries the grant option.
    pub(crate) fn grant_option_of(&self, user: &str, database: &str) -> Option<bool> {
        self.row_of(user, database).map(|row| row.1)
    }

    /// The `Db` column of every row `user` holds.
    pub(crate) fn databases_granted_to(&self, user: &str) -> Vec<String> {
        self.server
            .lock()
            .unwrap()
            .rows
            .iter()
            .filter(|row| row.user == user)
            .map(|row| row.database.clone())
            .collect()
    }

    /// The privileges and grant option of one row.
    fn row_of(&self, user: &str, database: &str) -> Option<(DatabasePrivilegeSet, bool)> {
        self.server
            .lock()
            .unwrap()
            .rows
            .iter()
            .find(|row| row.user == user && row.database == database)
            .map(|row| (row.privileges, row.grant_option))
    }

    /// The text between the first pair of `delimiter`s after `from`.
    fn between(statement: &str, delimiter: char, occurrence: usize) -> String {
        statement
            .split(delimiter)
            .nth(occurrence)
            .unwrap_or_default()
            .to_owned()
    }

    /// The privileges named by a `GRANT`/`REVOKE` list, between the verb and ` ON `.
    fn listed(statement: &str, verb: &str) -> DatabasePrivilegeSet {
        let list = statement
            .strip_prefix(verb)
            .and_then(|rest| rest.split(" ON ").next())
            .unwrap_or_default();
        let privileges: Vec<DatabasePrivilege> = list
            .split(", ")
            .map(|keyword| {
                DatabasePrivilege::ALL
                    .into_iter()
                    .find(|privilege| privilege.sql_keyword() == keyword)
                    .unwrap_or_else(|| panic!("the fake does not know the privilege {keyword:?}"))
            })
            .collect();

        DatabasePrivilegeSet::of(&privileges).unwrap()
    }

    /// The `Db` column as the real client prints it.
    fn batch_escaped(field: &str) -> String {
        field.replace('\\', "\\\\")
    }
}

impl DbHost for FakePrivilegeHost {
    /// Records `statement`, then answers as a server would.
    fn execute(&self, statement: &str) -> Result<String, DbError> {
        self.server
            .lock()
            .unwrap()
            .statements
            .push(statement.to_owned());

        if let Some((prefix, code, stderr)) = self.refusal.lock().unwrap().clone()
            && statement.starts_with(&prefix)
        {
            return Err(DbError::from_client(code, &stderr));
        }

        if let Some((prefix, printed)) = self.answer.lock().unwrap().clone()
            && statement.starts_with(&prefix)
        {
            return Ok(printed);
        }

        let mut server = self.server.lock().unwrap();

        if statement == "SHOW DATABASES" {
            return Ok(server.databases.join("\n"));
        }

        if statement.starts_with("SELECT COUNT(*) FROM mysql.user ") {
            let asked = Self::between(statement, '\'', 1);
            return Ok(u8::from(server.users.contains(&asked)).to_string());
        }

        if statement.starts_with("SELECT Db, Grant_priv, ") {
            let asked = Self::between(statement, '\'', 1);
            let expected_columns = std::iter::once(GRANT_OPTION_COLUMN.to_owned())
                .chain(
                    DatabasePrivilege::ALL
                        .into_iter()
                        .map(|p| privilege_column(p).to_owned()),
                )
                .collect::<Vec<String>>()
                .join(", ");
            assert!(
                statement.contains(&expected_columns),
                "the read must name the columns in the order the closed set renders"
            );

            let flag = |held: bool| if held { "Y" } else { "N" };
            return Ok(server
                .rows
                .iter()
                .filter(|row| row.user == asked)
                .map(|row| {
                    let mut fields = vec![
                        Self::batch_escaped(&row.database),
                        flag(row.grant_option).to_owned(),
                    ];
                    fields.extend(
                        DatabasePrivilege::ALL
                            .into_iter()
                            .map(|privilege| flag(row.privileges.contains(privilege)).to_owned()),
                    );
                    fields.join("\t")
                })
                .collect::<Vec<String>>()
                .join("\n"));
        }

        if statement.starts_with("CREATE USER ") {
            let created = Self::between(statement, '\'', 1);
            if server.users.contains(&created) {
                return Err(DbError::from_client(1, "ERROR 1396 (HY000): exists"));
            }
            server.users.push(created.clone());
            server
                .passwords
                .push((created, Self::between(statement, '\'', 5)));

            return Ok(String::new());
        }

        if statement.starts_with("DROP USER ") {
            let dropped = Self::between(statement, '\'', 1);
            if !server.users.contains(&dropped) {
                return Err(DbError::from_client(1, "ERROR 1396 (HY000): missing"));
            }
            server.users.retain(|held| *held != dropped);
            server.rows.retain(|row| row.user != dropped);
            server.passwords.retain(|(held, _)| *held != dropped);

            return Ok(String::new());
        }

        if let Some(rest) = statement.strip_prefix("GRANT ") {
            assert!(
                !rest.contains("GRANT OPTION") && !rest.starts_with("ALL"),
                "a privilege change must never grant the grant option or ALL: {statement}"
            );
            let pattern = Self::between(statement, '`', 1);
            let user = Self::between(statement, '\'', 1);
            let granted = Self::listed(statement, "GRANT ");
            let held = server
                .rows
                .iter()
                .position(|row| row.user == user && row.database == pattern);
            match held {
                Some(index) => {
                    let merged = DatabasePrivilegeSet::from_all_where(|privilege| {
                        server.rows[index].privileges.contains(privilege)
                            || granted.contains(privilege)
                    });
                    server.rows[index].privileges = merged;
                }
                None => server.rows.push(Row {
                    user,
                    database: pattern,
                    privileges: granted,
                    grant_option: false,
                }),
            }

            return Ok(String::new());
        }

        if statement.starts_with("REVOKE ") {
            // MariaDB's grammar takes one kind of privilege list per REVOKE: the combined
            // `ALL PRIVILEGES, GRANT OPTION` is a syntax error there (issue #450).
            if statement.starts_with("REVOKE ALL PRIVILEGES, ")
                || statement.contains(", GRANT OPTION ON ")
            {
                return Err(DbError::from_client(
                    1,
                    "ERROR 1064 (42000): You have an error in your SQL syntax",
                ));
            }
            let pattern = Self::between(statement, '`', 1);
            let user = Self::between(statement, '\'', 1);
            let Some(row) = server
                .rows
                .iter_mut()
                .find(|row| row.user == user && row.database == pattern)
            else {
                return Err(DbError::from_client(1, "ERROR 1141 (42000): no such grant"));
            };

            if statement.starts_with("REVOKE ALL PRIVILEGES ON ") {
                row.privileges = DatabasePrivilegeSet::empty();
            } else if statement.starts_with("REVOKE GRANT OPTION ON ") {
                row.grant_option = false;
            } else {
                let revoked = Self::listed(statement, "REVOKE ");
                row.privileges = row.privileges.difference(revoked);
            }

            // The server removes a row that holds nothing.
            server
                .rows
                .retain(|row| !row.privileges.is_empty() || row.grant_option);

            return Ok(String::new());
        }

        panic!("the fake was asked to run an unexpected statement: {statement}");
    }
}

/// The account every test in this file is about.
fn alice() -> AccountName {
    AccountName::parse("alice").expect("valid")
}

/// `alice`'s `reader` login.
pub(crate) fn reader() -> DbUserName {
    DbUserName::for_account(&alice(), "reader").expect("valid")
}

/// `alice`'s `shop` database.
pub(crate) fn shop() -> DatabaseName {
    DatabaseName::for_account(&alice(), "shop").expect("valid")
}

/// A request to create `alice`'s `reader` login.
pub(crate) fn reader_request() -> DatabaseUserRequest {
    DatabaseUserRequest {
        user: reader(),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    }
}
