//! Tests for [`RemoteCommand`]: the rendered text is a constant with typed
//! holes, and nothing but the holes varies.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::remote_database_name::RemoteDatabaseName;
use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::source_user_name::SourceUserName;

use super::RemoteCommand;

/// Characters a shell treats specially; none may appear in a hole's value, so
/// none may come from a hole.
const SHELL_SPECIAL: [char; 14] = [
    ' ', '\t', '\n', '\'', '"', '`', '$', ';', '|', '&', '<', '>', '(', ')',
];

/// A path hole.
fn path(text: &str) -> RemotePath {
    RemotePath::parse(text).unwrap()
}

/// Every variant, with holes filled.
fn every_command() -> Vec<RemoteCommand> {
    vec![
        RemoteCommand::EffectiveUid,
        RemoteCommand::HomeFilesystemUsage,
        RemoteCommand::OsRelease,
        RemoteCommand::DatabaseServerVersion,
        RemoteCommand::WebServerVersion,
        RemoteCommand::MailStorePresent,
        RemoteCommand::MaranPanelVersion,
        RemoteCommand::ListPasswd,
        RemoteCommand::ListNginxVhostFiles,
        RemoteCommand::ReadNginxVhostFile {
            path: path("/etc/nginx/conf.d/site.conf"),
        },
        RemoteCommand::ListApacheVhostFiles,
        RemoteCommand::ReadApacheVhostFile {
            path: path("/etc/apache2/sites-enabled/site.conf"),
        },
        RemoteCommand::ListDatabases,
        RemoteCommand::ReadCrontab {
            user: SourceUserName::parse("alice").unwrap(),
        },
        RemoteCommand::ListPhpVersions,
        RemoteCommand::HomeDiskUsage {
            home: path("/home/alice"),
        },
        RemoteCommand::CountMailboxes {
            home: path("/home/alice"),
        },
        RemoteCommand::DumpDatabase {
            database: RemoteDatabaseName::parse("shop_wp").unwrap(),
        },
    ]
}

#[test]
fn every_command_starts_with_the_same_prelude_and_discards_the_remote_stderr() {
    for command in every_command() {
        let text = command.render();

        assert!(
            text.starts_with("PATH=\"$PATH:/usr/local/sbin:/usr/sbin:/sbin\"; export PATH; "),
            "{command:?} lost the prelude: {text}"
        );
        assert!(text.contains("LC_ALL=C; export LC_ALL; { "), "{text}");
        assert!(text.ends_with("; } 2>/dev/null"), "{text}");
    }
}

#[test]
fn a_command_without_holes_renders_the_same_text_every_time() {
    assert_eq!(
        RemoteCommand::ListPasswd.render(),
        RemoteCommand::ListPasswd.render()
    );
    assert!(
        RemoteCommand::ListPasswd
            .render()
            .contains("{ getent passwd; }")
    );
    assert!(RemoteCommand::EffectiveUid.render().contains("{ id -u; }"));
}

#[test]
fn a_path_hole_is_the_validated_value_between_single_quotes_and_nothing_else_varies() {
    let one = RemoteCommand::ReadNginxVhostFile {
        path: path("/etc/nginx/conf.d/a.conf"),
    }
    .render();
    let other = RemoteCommand::ReadNginxVhostFile {
        path: path("/etc/nginx/conf.d/b.conf"),
    }
    .render();

    assert!(one.contains("cat -- '/etc/nginx/conf.d/a.conf'"), "{one}");
    assert_eq!(
        one.replace("a.conf", "b.conf"),
        other,
        "only the hole may differ between two renders"
    );
}

#[test]
fn the_user_hole_is_quoted_and_the_home_hole_fills_every_place_it_belongs() {
    let crontab = RemoteCommand::ReadCrontab {
        user: SourceUserName::parse("alice").unwrap(),
    }
    .render();
    let disk = RemoteCommand::HomeDiskUsage {
        home: path("/home/alice"),
    }
    .render();
    let mail = RemoteCommand::CountMailboxes {
        home: path("/home/alice"),
    }
    .render();

    assert!(crontab.contains("crontab -u 'alice' -l"), "{crontab}");
    assert!(disk.contains("du -skx -- '/home/alice'"), "{disk}");
    assert!(
        mail.contains("find '/home/alice/Maildir' '/home/alice/mail' -maxdepth 3"),
        "{mail}"
    );
}

#[test]
fn a_hole_value_cannot_hold_a_shell_special_character() {
    // The grammars refuse these before a RemoteCommand can be built; this pins
    // the refusal from this side, so widening a grammar fails here too.
    for special in SHELL_SPECIAL {
        let candidate = format!("/home/a{special}b");

        assert!(
            RemotePath::parse(&candidate).is_err(),
            "RemotePath accepted {candidate:?}"
        );
        assert!(
            SourceUserName::parse(&format!("a{special}b")).is_err(),
            "SourceUserName accepted a{special}b"
        );
    }
}

#[test]
fn an_accepted_hole_value_contains_no_shell_special_character() {
    // Inverse control: the values the tests build commands from pass the
    // grammar, and rendering them adds no character from the special set that
    // the constant text does not already contain.
    let rendered = RemoteCommand::ReadNginxVhostFile {
        path: path("/etc/nginx/sites-enabled/example.org_80.conf"),
    }
    .render();
    let constant = RemoteCommand::ReadNginxVhostFile {
        path: path("/etc/nginx/x"),
    }
    .render();

    for special in SHELL_SPECIAL {
        assert_eq!(
            rendered.matches(special).count(),
            constant.matches(special).count(),
            "the hole added {special:?}"
        );
    }
}

#[test]
fn no_command_writes_removes_or_signals_anything_on_the_source() {
    for command in every_command() {
        let text = command.render();

        for forbidden in [
            " rm ",
            " mv ",
            " cp ",
            "chmod",
            "chown",
            "kill",
            "tee ",
            " > /",
            ">>",
            "mkdir",
            "touch",
            "sudo",
            "systemctl",
            "service ",
        ] {
            assert!(
                !text.contains(forbidden),
                "{command:?} contains {forbidden:?}: {text}"
            );
        }
    }
}

#[test]
fn the_database_listing_leaves_out_the_servers_own_schemas() {
    let text = RemoteCommand::ListDatabases.render();

    for schema in ["information_schema", "mysql", "performance_schema", "sys"] {
        assert!(text.contains(&format!("'{schema}'")), "{text}");
    }
}

#[test]
fn the_listings_and_reads_name_the_directories_of_both_families() {
    let nginx = RemoteCommand::ListNginxVhostFiles.render();
    let apache = RemoteCommand::ListApacheVhostFiles.render();

    assert!(nginx.contains("/etc/nginx/conf.d") && nginx.contains("/etc/nginx/sites-enabled"));
    assert!(apache.contains("/etc/apache2/sites-enabled") && apache.contains("/etc/httpd/conf.d"));
}

#[test]
fn the_dump_command_names_the_database_once_between_single_quotes_after_the_end_of_options() {
    let text = RemoteCommand::DumpDatabase {
        database: RemoteDatabaseName::parse("shop_wp").unwrap(),
    }
    .render();

    assert_eq!(text.matches("shop_wp").count(), 1);
    assert!(text.contains(" -- 'shop_wp'"), "{text}");
}

#[test]
fn the_dump_command_leaves_out_what_the_restricted_loader_would_refuse() {
    let text = RemoteCommand::DumpDatabase {
        database: RemoteDatabaseName::parse("shop_wp").unwrap(),
    }
    .render();

    // No triggers (a definer the loader refuses), one transaction so a live
    // source is dumped consistently, binary values as hexadecimal text.
    assert!(text.contains("--skip-triggers"));
    assert!(text.contains("--single-transaction"));
    assert!(text.contains("--hex-blob"));
    assert!(!text.contains("--routines") && !text.contains("--events"));
    // Inverse control: it dumps one named database and not every database.
    assert!(!text.contains("--all-databases") && !text.contains("--databases"));
}
