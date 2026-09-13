//! The in-memory [`DbHost`] the database tests decide against.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host spawns a client against a live server: a unit test cannot create a
//! database, and a suite that tried would pass or fail on whether the build
//! container happens to run one. What a unit test CAN pin is the decision — which
//! statements an operation chooses to send, in which order, which it refuses to
//! send at all, and what it makes of each answer.
//!
//! The fake keeps the server's database list in memory and applies the create
//! and drop statements to it, so "create twice" and "drop twice" converge here
//! for the same reason they converge against a real server rather than because
//! the fake was told the answer.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::HashMap;
use std::sync::Mutex;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::model::create_database_request::CreateDatabaseRequest;

/// The size the fake reports for every database, unless a test sets another.
const DEFAULT_SIZE_BYTES: u64 = 4096;

/// A [`DbHost`] that keeps a server's database list in memory.
pub(crate) struct FakeDbHost {
    /// The databases the "server" holds, in the order they were added.
    databases: Mutex<Vec<String>>,
    /// The users the "server" holds, so a test can prove one was created or
    /// removed without parsing a grant table.
    users: Mutex<Vec<String>>,
    /// The password the "server" currently holds for each user, so a test can
    /// prove a reset REPLACED one rather than merely running without failing.
    passwords: Mutex<HashMap<String, String>>,
    /// What the user-existence query answers with, when a test overrode it.
    user_count: Mutex<Option<String>>,
    /// Every statement the fake was asked to run, in order.
    statements: Mutex<Vec<String>>,
    /// The exit status and standard error every statement fails with, when a
    /// test installed a failure.
    failure: Mutex<Option<(i32, String)>>,
    /// What the size query answers with.
    size: Mutex<String>,
}

impl FakeDbHost {
    /// A host whose server holds nothing and refuses nothing.
    pub(crate) fn new() -> Self {
        Self {
            databases: Mutex::new(Vec::new()),
            users: Mutex::new(Vec::new()),
            passwords: Mutex::new(HashMap::new()),
            user_count: Mutex::new(None),
            statements: Mutex::new(Vec::new()),
            failure: Mutex::new(None),
            size: Mutex::new(DEFAULT_SIZE_BYTES.to_string()),
        }
    }

    /// A host whose server already holds `name`.
    pub(crate) fn with_existing(name: &str) -> Self {
        Self::with_existing_many(&[name])
    }

    /// A host whose server already holds each of `names`, in that order.
    ///
    /// The order is given rather than sorted, so a test can tell a listing that
    /// sorts from one that happens to echo the server back.
    pub(crate) fn with_existing_many(names: &[&str]) -> Self {
        let host = Self::new();
        *host.databases.lock().unwrap() = names.iter().map(|name| (*name).to_owned()).collect();

        host
    }

    /// A host whose server refuses every statement with `code` and `stderr`.
    ///
    /// `code` is what the client exits with; the classification is the
    /// production one, so a test that pins a variant is pinning the real
    /// mapping rather than the fake's opinion of it.
    pub(crate) fn failing_with(code: i32, stderr: &str) -> Self {
        let host = Self::new();
        *host.failure.lock().unwrap() = Some((code, stderr.to_owned()));

        host
    }

    /// Makes the size query answer `printed`.
    pub(crate) fn set_size_output(&self, printed: &str) {
        *self.size.lock().unwrap() = printed.to_owned();
    }

    /// Every statement the fake was asked to run, in order.
    pub(crate) fn statements(&self) -> Vec<String> {
        self.statements.lock().unwrap().clone()
    }

    /// The databases the "server" holds now.
    pub(crate) fn databases(&self) -> Vec<String> {
        self.databases.lock().unwrap().clone()
    }

    /// The users the "server" holds now.
    pub(crate) fn users(&self) -> Vec<String> {
        self.users.lock().unwrap().clone()
    }

    /// Makes the user-existence query answer `printed` instead of counting.
    ///
    /// The only way to reach the parsing branch of that check: a real server
    /// prints a number, and the fake would too, so a test of what happens when
    /// it does not has to install the answer.
    pub(crate) fn set_user_count_output(&self, printed: &str) {
        *self.user_count.lock().unwrap() = Some(printed.to_owned());
    }

    /// The password the "server" now holds for `user`, if any.
    pub(crate) fn password_of(&self, user: &str) -> Option<String> {
        self.passwords.lock().unwrap().get(user).cloned()
    }

    /// The identifier between the first pair of `delimiter`s in `statement`.
    fn quoted(statement: &str, delimiter: char) -> String {
        Self::nth_quoted(statement, delimiter, 1)
    }

    /// The `index`th `delimiter`-separated field of `statement`, counting the
    /// text before the first delimiter as field zero.
    ///
    /// Needed because a credential statement carries three quoted fields —
    /// `'user'@'localhost' IDENTIFIED BY 'password'` — and the fake has to read
    /// the third to prove a reset changed it. Reading it by position rather than
    /// with a regex keeps the fake dumber than the code it judges, which is what
    /// stops it agreeing with a wrong implementation.
    fn nth_quoted(statement: &str, delimiter: char, index: usize) -> String {
        statement
            .split(delimiter)
            .nth(index)
            .unwrap_or_default()
            .to_owned()
    }
}

impl DbHost for FakeDbHost {
    /// Records `statement`, then answers as a server would.
    ///
    /// Unknown statements panic rather than answering blandly: a fake that
    /// shrugs at a statement nobody expected is a fake that lets an operation
    /// send anything and still pass.
    fn execute(&self, statement: &str) -> Result<String, DbError> {
        self.statements.lock().unwrap().push(statement.to_owned());

        let failure = self.failure.lock().unwrap().clone();
        if let Some((code, stderr)) = failure {
            return Err(DbError::from_client(code, &stderr));
        }

        if statement == "SHOW DATABASES" {
            return Ok(self.databases.lock().unwrap().join("\n"));
        }

        if statement.starts_with("CREATE DATABASE ") {
            self.databases
                .lock()
                .unwrap()
                .push(Self::quoted(statement, '`'));

            return Ok(String::new());
        }

        if statement.starts_with("DROP DATABASE ") {
            let dropped = Self::quoted(statement, '`');
            self.databases
                .lock()
                .unwrap()
                .retain(|held| *held != dropped);

            return Ok(String::new());
        }

        if statement.starts_with("CREATE USER ") {
            let created = Self::quoted(statement, '\'');
            let mut users = self.users.lock().unwrap();
            if !users.contains(&created) {
                users.push(created.clone());
                // Only for a user that was actually created. `CREATE USER IF NOT
                // EXISTS` leaves an existing user's password alone, and a fake
                // that overwrote it here would report a create as having reset
                // a credential it did not touch.
                self.passwords
                    .lock()
                    .unwrap()
                    .insert(created, Self::nth_quoted(statement, '\'', 5));
            }

            return Ok(String::new());
        }

        if statement == "SELECT User FROM mysql.user WHERE Host = 'localhost'" {
            return Ok(self.users.lock().unwrap().join("\n"));
        }

        if statement.starts_with("SELECT COUNT(*) FROM mysql.user ") {
            if let Some(printed) = self.user_count.lock().unwrap().clone() {
                return Ok(printed);
            }

            let asked = Self::quoted(statement, '\'');
            let held = self.users.lock().unwrap().contains(&asked);

            return Ok(if held { "1".to_owned() } else { "0".to_owned() });
        }

        if statement.starts_with("ALTER USER ") {
            let altered = Self::quoted(statement, '\'');
            if !self.users.lock().unwrap().contains(&altered) {
                panic!("the fake was asked to alter a user it does not hold: {altered}");
            }

            self.passwords
                .lock()
                .unwrap()
                .insert(altered, Self::nth_quoted(statement, '\'', 5));

            return Ok(String::new());
        }

        if statement.starts_with("DROP USER ") {
            let dropped = Self::quoted(statement, '\'');
            self.users.lock().unwrap().retain(|held| *held != dropped);

            return Ok(String::new());
        }

        if statement.starts_with("SELECT COALESCE(") {
            return Ok(self.size.lock().unwrap().clone());
        }

        if statement.starts_with("GRANT ") {
            return Ok(String::new());
        }

        panic!("the fake was asked to run an unexpected statement: {statement}");
    }
}

/// The account every test in this folder is about.
pub(crate) fn account() -> AccountName {
    AccountName::parse("alice").expect("valid")
}

/// A request for `alice`'s `shop` database, owned by `alice`'s `shop` user.
pub(crate) fn shop_request() -> CreateDatabaseRequest {
    let account = account();

    CreateDatabaseRequest {
        database: DatabaseName::for_account(&account, "shop").expect("valid"),
        user: DbUserName::for_account(&account, "shop").expect("valid"),
        password: Password::parse("Gen3rated-pw").expect("valid"),
    }
}

/// `alice`'s `shop` database name.
pub(crate) fn shop_database() -> DatabaseName {
    DatabaseName::for_account(&account(), "shop").expect("valid")
}

/// `alice`'s `shop` database user.
pub(crate) fn shop_user() -> DbUserName {
    DbUserName::for_account(&account(), "shop").expect("valid")
}
