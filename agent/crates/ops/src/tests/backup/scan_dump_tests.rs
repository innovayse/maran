//! The restricted loader's pre-scan refuses a hostile dump and accepts a clean one.
//!
//! Every refusal case has the inverse beside it: the clean dump at the top is the
//! positive control, and several hostile texts have a look-alike that must pass,
//! so a scanner mutated to refuse everything fails here too.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::dump_lexer::DumpLexer;

use super::*;

/// The database every fixture dump is for.
fn database() -> DatabaseName {
    let account = AccountName::parse("alice").expect("the fixture name is valid");

    DatabaseName::for_account(&account, "shop").expect("the fixture name is valid")
}

/// Scans `text` as a dump of the fixture database.
fn scan(text: &str) -> Result<(), BackupError> {
    let mut bytes = text.as_bytes();

    scan_dump(&mut bytes, &database())
}

/// Why `text` is refused; fails the test when it is not.
fn refusal(text: &str) -> DumpRefusal {
    match scan(text) {
        Err(BackupError::RestrictedLoadRefused { reason }) => reason,
        other => panic!("expected a refusal of {text:?}, got {other:?}"),
    }
}

/// Fails the test when `text` is refused.
fn accepted(text: &str) {
    if let Err(error) = scan(text) {
        panic!("a clean text was refused: {text:?}: {error}");
    }
}

/// A dump as the dump tool writes one, with the lines that matter to a scanner.
const CLEAN_DUMP: &str = "/*M!999999\\- enable the sandbox mode */ \n\
-- MariaDB dump 10.19  Distrib 10.11.6-MariaDB, for debian-linux-gnu (x86_64)\n\
--\n\
-- Host: localhost    Database: alice_shop\n\
-- ------------------------------------------------------\n\
-- Server version\t10.11.6-MariaDB-0+deb12u1\n\
\n\
/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;\n\
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;\n\
/*!40101 SET NAMES utf8mb4 */;\n\
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;\n\
/*!40103 SET TIME_ZONE='+00:00' */;\n\
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;\n\
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;\n\
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;\n\
/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;\n\
\n\
CREATE DATABASE /*!32312 IF NOT EXISTS*/ `alice_shop` /*!40100 DEFAULT CHARACTER SET utf8mb4 */;\n\
\n\
USE `alice_shop`;\n\
\n\
DROP TABLE IF EXISTS `orders`;\n\
/*!40101 SET @saved_cs_client     = @@character_set_client */;\n\
/*!40101 SET character_set_client = utf8mb4 */;\n\
CREATE TABLE `orders` (\n\
  `id` int(11) NOT NULL AUTO_INCREMENT,\n\
  `note` varchar(255) DEFAULT NULL COMMENT 'it''s a note; with a semicolon',\n\
  `definer` text,\n\
  PRIMARY KEY (`id`)\n\
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;\n\
/*!40101 SET character_set_client = @saved_cs_client */;\n\
\n\
LOCK TABLES `orders` WRITE;\n\
/*!40000 ALTER TABLE `orders` DISABLE KEYS */;\n\
INSERT INTO `orders` VALUES (1,'a \\' quote; DROP DATABASE mysql; -- not a comment','DEFINER OUTFILE system \\\\! id'),(2,NULL,0x4142);\n\
/*!40000 ALTER TABLE `orders` ENABLE KEYS */;\n\
UNLOCK TABLES;\n\
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;\n\
/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;\n\
/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;\n\
-- Dump completed on 2026-11-07  9:00:00\n";

// ---------------------------------------------------------------------------
// The positive controls
// ---------------------------------------------------------------------------

#[test]
fn a_clean_dump_with_every_header_and_footer_line_is_accepted() {
    accepted(CLEAN_DUMP);
}

#[test]
fn a_clean_dump_is_accepted_when_read_one_byte_at_a_time() {
    let mut lexer = DumpLexer::new("alice_shop");
    for byte in CLEAN_DUMP.as_bytes() {
        lexer.feed(&[*byte]).unwrap();
    }

    lexer.finish().unwrap();
}

#[test]
fn a_hostile_text_is_refused_the_same_way_whatever_the_read_boundaries() {
    let hostile = "INSERT INTO t VALUES (1);\n/*!50000 \\! id */;\n";
    let mut lexer = DumpLexer::new("alice_shop");
    let mut outcome = Ok(());
    for byte in hostile.as_bytes() {
        outcome = lexer.feed(&[*byte]);
        if outcome.is_err() {
            break;
        }
    }

    assert_eq!(outcome, Err(DumpRefusal::ClientCommand));
}

#[test]
fn text_that_looks_hostile_inside_a_comment_or_a_quote_is_accepted() {
    accepted("# \\! id\nINSERT INTO t VALUES (1);\n");
    accepted("-- \\! id and GRANT ALL ON *.* and DEFINER=x\nINSERT INTO t VALUES (1);\n");
    accepted("/* \\! id; GRANT ALL ON *.* */ INSERT INTO t VALUES (1);\n");
    accepted("INSERT INTO t VALUES ('GRANT ALL ON *.* TO x; system id; \\! id');\n");
    accepted("INSERT INTO t VALUES ('a\r\nsystem id\r\nb');\r\n");
    accepted("INSERT INTO `t` (`definer`, `outfile`, `system`) VALUES (1,2,3);\n");
}

#[test]
fn a_last_statement_without_a_terminator_is_judged_like_any_other() {
    accepted("INSERT INTO t VALUES (1)");
    assert_eq!(
        refusal("GRANT ALL ON *.* TO x"),
        DumpRefusal::ForbiddenStatement
    );
}

#[test]
fn a_large_dump_is_read_to_its_end() {
    let row = "INSERT INTO `orders` VALUES (1,'some text; with \\' escapes',NULL);\n";
    let dump = row.repeat(80_000);

    accepted(&dump);
}

// ---------------------------------------------------------------------------
// The client's own commands: the one refusal no database privilege replaces
// ---------------------------------------------------------------------------

#[test]
fn a_backslash_command_anywhere_outside_a_quote_is_refused() {
    for text in [
        "\\! id\n",
        "INSERT INTO t VALUES (1); \\! id\n",
        "INSERT INTO t VALUES (1)\n\\! id\n",
        "INSERT INTO t VALUES (1) \\g\n",
        "--\\! id\n",
        "SET @a=1 /* x */ \\. /etc/passwd\n",
    ] {
        assert_eq!(refusal(text), DumpRefusal::ClientCommand, "{text:?}");
    }
}

#[test]
fn a_named_client_command_is_refused() {
    for text in [
        "system id\n",
        "SYSTEM id;\n",
        "INSERT INTO t VALUES (1);\nsystem id\n",
        "source /etc/passwd;\n",
        "tee /tmp/x;\n",
        "pager sh;\n",
        "delimiter ;;\n",
        "DELIMITER ;;\n",
        "connect other;\n",
        "charset latin1;\n",
    ] {
        assert_eq!(refusal(text), DumpRefusal::ClientCommand, "{text:?}");
    }
}

#[test]
fn a_command_hidden_in_an_executable_comment_is_refused_whatever_its_version() {
    assert_eq!(refusal("/*!99999 \\! id */;"), DumpRefusal::ClientCommand);
    assert_eq!(refusal("/*!999999 \\! id */;"), DumpRefusal::ClientCommand);
    assert_eq!(
        refusal("/*M!999999 \\. /etc/x */;"),
        DumpRefusal::ClientCommand
    );
    assert_eq!(
        refusal("/*!50000 system id */;"),
        DumpRefusal::ClientCommand
    );
}

#[test]
fn a_comment_no_server_runs_may_not_hold_a_quote_or_a_statement_end() {
    accepted("/*M!999999\\- enable the sandbox mode */\nINSERT INTO t VALUES (1);\n");
    assert_eq!(refusal("/*!999999 ' */ ;"), DumpRefusal::Malformed);
    assert_eq!(
        refusal("/*!999999 ; */ INSERT INTO t VALUES (1);"),
        DumpRefusal::Malformed
    );
}

// ---------------------------------------------------------------------------
// Spellings the client and the server read two ways
// ---------------------------------------------------------------------------

#[test]
fn a_backslash_inside_a_backtick_name_is_refused_because_the_client_escapes_it() {
    // The client takes the backslash as escaping the closing backtick and stays
    // in the name; a scanner that closed it would read the next backtick as an
    // opening one, with the client already outside, and the command in between
    // would be invisible to it.
    assert_eq!(
        refusal("INSERT INTO `a\\` VALUES (1); \\d x `;"),
        DumpRefusal::Malformed
    );
    assert_eq!(
        refusal("CREATE TABLE `a\\` (a int);"),
        DumpRefusal::Malformed
    );
    accepted("CREATE TABLE `a b` (`c``d` int);");
}

#[test]
fn a_control_byte_after_two_dashes_is_refused_because_only_the_server_starts_a_comment() {
    assert_eq!(
        refusal("INSERT INTO t VALUES (1); --\u{1} ; \\d x\n"),
        DumpRefusal::Malformed
    );
    accepted("INSERT INTO t VALUES (1); --\t; \\d x\nINSERT INTO t VALUES (2);\n");
    accepted("INSERT INTO t VALUES (1); --\r\nINSERT INTO t VALUES (2);\n");
    accepted("INSERT INTO t VALUES (3 --1);\n");
}

#[test]
fn a_mariadb_comment_may_not_hold_what_a_mysql_client_would_read_differently() {
    // A MySQL client reads `/*M!` as an ordinary comment: the quote below ends at
    // the `*/` for it and never closes for a scanner that reads the comment as code.
    for text in [
        "/*M!100000 ' */ ; \\d x ; '",
        "/*M!100000 \" */ ; \\d x ; \"",
        "/*M!100000 ` */ ; \\d x ; `",
        "/*M!100000 ; */ INSERT INTO t VALUES (1);",
        "/*M!100000 # */\nINSERT INTO t VALUES (1);",
        "/*M!100000 -- x\n */ INSERT INTO t VALUES (1);",
        "/*M!100000 /* x */ INSERT INTO t VALUES (1);",
    ] {
        assert_eq!(refusal(text), DumpRefusal::Malformed, "{text:?}");
    }
    accepted("/*M!100616 SET NOTE_VERBOSITY=0 */;\nINSERT INTO t VALUES (1);");
    accepted("/*!100616 SET @a='x; y' */;\nINSERT INTO t VALUES (1);");
    // The restriction ends with the comment.
    accepted("/*M!100616 SET NOTE_VERBOSITY=0 */;\nINSERT INTO t VALUES ('a');");
}

#[test]
fn a_use_with_anything_after_the_name_is_refused_because_the_client_reads_its_own_argument() {
    assert_eq!(refusal("USE alice_shop other;"), DumpRefusal::OtherDatabase);
    assert_eq!(refusal("USE alice_shop.x;"), DumpRefusal::OtherDatabase);
    accepted("USE `alice_shop`;");
}

#[test]
fn a_character_set_whose_characters_can_hold_a_backslash_is_refused_in_a_set() {
    for text in [
        "SET NAMES gbk;",
        "SET NAMES 'sjis';",
        "SET NAMES big5 COLLATE big5_chinese_ci;",
        "SET character_set_client = cp932;",
        "SET character_set_client = 'g' 'bk';",
        "SET @x = 'ucs2';",
        "SET NAMES utf16le;",
        "SET collation_connection = gbk_chinese_ci;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::UnsafeSet, "{text:?}");
    }
    accepted("SET NAMES utf8mb4;");
    accepted("SET NAMES latin1 COLLATE latin1_swedish_ci;");
    accepted("SET NAMES binary;");
    accepted("SET character_set_client = utf8mb3;");
}

// ---------------------------------------------------------------------------
// Statements a dump does not carry
// ---------------------------------------------------------------------------

#[test]
fn a_statement_that_is_not_on_the_list_is_refused() {
    for text in [
        "GRANT ALL PRIVILEGES ON *.* TO 'x'@'%';",
        "CREATE USER 'x'@'%' IDENTIFIED BY 'y';",
        "DROP USER 'x'@'%';",
        "ALTER USER root IDENTIFIED BY 'y';",
        "ALTER DATABASE alice_shop CHARACTER SET latin1;",
        "INSTALL PLUGIN x SONAME 'y.so';",
        "FLUSH PRIVILEGES;",
        "SHUTDOWN;",
        "KILL 1;",
        "SELECT 1;",
        "CALL p();",
        "DROP VIEW v;",
        "(SELECT 1);",
        "`orders` = 1;",
        "1;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::ForbiddenStatement, "{text:?}");
    }
}

#[test]
fn a_statement_that_reads_or_writes_a_file_is_refused() {
    for text in [
        "SELECT * FROM t INTO OUTFILE '/tmp/x';",
        "SELECT 'x' INTO DUMPFILE '/tmp/x';",
        "LOAD DATA INFILE '/etc/passwd' INTO TABLE t;",
        "LOAD DATA LOCAL INFILE '/etc/passwd' INTO TABLE t;",
        "INSERT INTO t SELECT LOAD_FILE('/etc/shadow');",
        "INSERT INTO t VALUES (SLEEP(100000));",
        "CREATE TABLE t (a int) DATA DIRECTORY='/etc';",
        "CREATE TABLE t (a int) ENGINE=FEDERATED CONNECTION='mysql://u@h/d/t';",
    ] {
        assert_eq!(refusal(text), DumpRefusal::ForbiddenKeyword, "{text:?}");
    }
}

// ---------------------------------------------------------------------------
// Objects the loader's user could only create broken
// ---------------------------------------------------------------------------

#[test]
fn a_definer_clause_is_refused_wherever_it_appears() {
    for text in [
        "/*!50003 CREATE*/ /*!50017 DEFINER=`root`@`localhost`*/ /*!50003 TRIGGER `t` BEFORE INSERT ON `x` FOR EACH ROW SET NEW.a = 1 */;",
        "CREATE DEFINER=`root`@`localhost` PROCEDURE p() SELECT 1;",
        "/*!50001 CREATE ALGORITHM=UNDEFINED */ /*!50013 DEFINER=`root`@`localhost` SQL SECURITY DEFINER */ /*!50001 VIEW `v` AS select 1 */;",
        "ALTER DEFINER=root@localhost VIEW v AS SELECT 1;",
        "INSERT INTO t SELECT 1 FROM x DEFINER;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::DefinerClause, "{text:?}");
    }
}

#[test]
fn a_trigger_routine_event_or_view_is_refused() {
    for text in [
        "CREATE TRIGGER trg BEFORE INSERT ON t FOR EACH ROW SET NEW.a = 1;",
        "CREATE PROCEDURE p() SELECT 1;",
        "CREATE FUNCTION f() RETURNS int RETURN 1;",
        "CREATE EVENT e ON SCHEDULE EVERY 1 DAY DO SELECT 1;",
        "CREATE VIEW v AS SELECT 1;",
        "CREATE ALGORITHM=MERGE VIEW v AS SELECT 1;",
        "CREATE SQL SECURITY INVOKER VIEW v AS SELECT 1;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::StoredProgram, "{text:?}");
    }
}

// ---------------------------------------------------------------------------
// Other databases
// ---------------------------------------------------------------------------

#[test]
fn a_statement_naming_another_database_is_refused() {
    for text in [
        "USE `alice_other`;",
        "USE mysql;",
        "USE;",
        "CREATE DATABASE `bob_db`;",
        "CREATE DATABASE IF NOT EXISTS `mysql`;",
        "CREATE SCHEMA other;",
        "DROP DATABASE IF EXISTS `mysql`;",
        "DROP SCHEMA `alice_shop2`;",
        "USE `alice_shop``mysql`;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::OtherDatabase, "{text:?}");
    }
}

#[test]
fn a_statement_naming_the_database_being_loaded_is_accepted() {
    accepted("USE `alice_shop`;");
    accepted("USE alice_shop;");
    accepted("CREATE DATABASE IF NOT EXISTS `alice_shop`;");
    accepted("DROP DATABASE IF EXISTS `alice_shop`;");
}

// ---------------------------------------------------------------------------
// SET
// ---------------------------------------------------------------------------

#[test]
fn a_set_of_a_global_or_a_privileged_variable_is_refused() {
    for text in [
        "SET GLOBAL sql_mode='';",
        "SET @@GLOBAL.sql_mode='';",
        "SET @@global.max_connections=1;",
        "SET PERSIST x=1;",
        "SET PASSWORD = 'x';",
        "SET @@SESSION.SQL_LOG_BIN=0;",
        "SET SESSION sql_log_bin=0;",
        "SET @@session.gtid_domain_id=1;",
        "SET sql_mode=0x4e4f;",
        "SET sql_mode=_utf8'x';",
        "SET sql_mode=X'4e4f';",
        "SET @a=(SELECT 1);",
        "SET;",
    ] {
        assert_eq!(refusal(text), DumpRefusal::UnsafeSet, "{text:?}");
    }
}

#[test]
fn a_set_that_changes_how_a_quote_is_read_is_refused_even_when_spelled_in_pieces() {
    assert_eq!(
        refusal("SET sql_mode='NO_BACKSLASH_ESCAPES';"),
        DumpRefusal::ForbiddenKeyword
    );
    assert_eq!(
        refusal("SET SQL_MODE='ansi,no_backslash_escapes';"),
        DumpRefusal::ForbiddenKeyword
    );
    assert_eq!(
        refusal("SET sql_mode='NO_BACKSLASH_' 'ESCAPES';"),
        DumpRefusal::ForbiddenKeyword
    );
    assert_eq!(
        refusal("SET sql_mode=NO_BACKSLASH_ESCAPES;"),
        DumpRefusal::ForbiddenKeyword
    );
}

#[test]
fn a_set_with_too_many_parts_is_refused() {
    let text = format!("SET {};", "@a=1, ".repeat(40).trim_end_matches(", "));

    assert_eq!(refusal(&text), DumpRefusal::UnsafeSet);
}

#[test]
fn a_set_of_the_variables_a_dump_sets_is_accepted() {
    accepted("SET NAMES utf8mb4 COLLATE utf8mb4_general_ci;");
    accepted("SET foreign_key_checks=0, unique_checks=0;");
    accepted("SET @@SESSION.FOREIGN_KEY_CHECKS=0;");
    accepted("SET SESSION time_zone='+00:00';");
    accepted("SET @x = @@sql_mode;");
}

// ---------------------------------------------------------------------------
// Text that cannot be read as statements
// ---------------------------------------------------------------------------

#[test]
fn text_that_ends_inside_a_quote_or_a_comment_is_refused() {
    for text in [
        "INSERT INTO t VALUES ('abc",
        "INSERT INTO t VALUES (\"abc",
        "CREATE TABLE `t",
        "/* abc",
        "/*!40101 SET NAMES utf8",
        "/*!999999 abc",
        "INSERT INTO t VALUES ('a\\",
    ] {
        assert_eq!(refusal(text), DumpRefusal::Malformed, "{text:?}");
    }
}

#[test]
fn a_nul_byte_outside_a_literal_is_refused_and_inside_one_is_accepted() {
    let mut hostile = b"INSERT\0 INTO t VALUES (1);".as_slice();
    let refused = scan_dump(&mut hostile, &database());
    assert!(matches!(
        refused,
        Err(BackupError::RestrictedLoadRefused {
            reason: DumpRefusal::Malformed
        })
    ));

    let mut clean = b"INSERT INTO t VALUES ('a\0b');".as_slice();
    assert!(scan_dump(&mut clean, &database()).is_ok());
}

#[test]
fn a_dump_that_cannot_be_read_is_a_load_failure_and_not_a_refusal() {
    struct Unreadable;
    impl std::io::Read for Unreadable {
        fn read(&mut self, _buffer: &mut [u8]) -> std::io::Result<usize> {
            Err(std::io::Error::other("unreadable"))
        }
    }

    let error = scan_dump(&mut Unreadable, &database()).unwrap_err();

    assert_eq!(error, BackupError::LoadFailed { status: -1 });
}
