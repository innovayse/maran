//! Tests for `SftpJailStatus::evaluate`: does the live `sshd_config` still
//! carry the block that jails an SFTP login.
//!
//! Every fixture in this file is the block `installer/lib/86-sftp.sh`'s
//! `render_sshd_block` actually writes (its marker comments, its `Match
//! Group` line and its four directives, in its order) — never a shape this
//! test invented independently of the installer.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SftpJailStatus;

/// The group name every fixture below checks against, matching what
/// `DistroAdapter::sftp_group` answers on both families.
const GROUP: &str = "maran-sftp";

/// The exact block `render_sshd_block` writes, embedded in the rest of a
/// plausible `sshd_config` — content the installer never touches both BEFORE
/// the begin marker and AFTER the end marker (a trailing `Subsystem` line),
/// so the test proves the block is found and bounded correctly inside a real
/// file rather than only when it is the whole input or the last thing in it.
/// The installer itself never actually appends after its own block
/// (`installer/lib/86-sftp.sh` puts it at the END of the file), so the
/// trailing line here is deliberately testing beyond what the installer
/// produces today — a hand edit, or a future version of the installer, could
/// still add one.
const CONFIG_WITH_INTACT_BLOCK: &str = "\
# This is the sshd server system-wide configuration file.
Port 22
PermitRootLogin no

# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
Subsystem sftp /usr/lib/openssh/sftp-server
";

/// The same file with no Maran block at all — the shape a fresh install with
/// SFTP never enabled, or a rewrite that dropped the block entirely, leaves.
const CONFIG_WITH_NO_BLOCK: &str = "\
# This is the sshd server system-wide configuration file.
Port 22
PermitRootLogin no
";

#[test]
fn the_exact_installer_block_is_intact() {
    assert_eq!(
        SftpJailStatus::evaluate(CONFIG_WITH_INTACT_BLOCK, GROUP),
        SftpJailStatus::Intact
    );
}

/// **The inverse of the case above, and the one this check exists for.** The
/// README's own defect is exactly this: the block removed by hand or by a
/// package upgrade, with nothing in the panel able to see it. A check with no
/// failing direction would be a check that never actually ran.
#[test]
fn a_config_with_no_maran_block_at_all_is_drifted() {
    let status = SftpJailStatus::evaluate(CONFIG_WITH_NO_BLOCK, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a config with no block at all must not read as intact");
    };
    assert_eq!(missing.len(), 1, "one finding: the whole block is gone");
    assert!(missing[0].contains("BEGIN Maran SFTP"));
}

#[test]
fn a_block_with_the_end_marker_but_no_begin_marker_is_drifted() {
    let config = "Port 22\nMatch Group maran-sftp\n    ChrootDirectory %h\n# END Maran SFTP\n";

    assert!(matches!(
        SftpJailStatus::evaluate(config, GROUP),
        SftpJailStatus::Drifted { .. }
    ));
}

/// The mirror image of the case above: the begin marker is there but the file
/// ends before the installer's own end marker — a truncated write, or a hand
/// edit that deleted only the closing line. `extract_block` must refuse this
/// exactly as it refuses the reverse, rather than treating "found a begin
/// marker" as enough.
#[test]
fn a_block_with_the_begin_marker_but_no_end_marker_is_drifted() {
    let config = "Port 22\n# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers\nMatch Group maran-sftp\n    ChrootDirectory %h\n    ForceCommand internal-sftp\n    AllowTcpForwarding no\n    X11Forwarding no\n";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block with no end marker must not read as intact");
    };
    assert_eq!(
        missing.len(),
        1,
        "one finding: the block cannot be located at all"
    );
    assert!(missing[0].contains("END Maran SFTP"));
}

/// Removing exactly the directive the README names — the one that turns a
/// jailed transfer into a shell — must be caught on its own, not only when
/// the whole block disappears.
#[test]
fn a_block_missing_only_forcecommand_names_that_one_directive() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block with a directive hand-edited away must not read as intact");
    };
    assert_eq!(missing, vec!["forcecommand internal-sftp".to_owned()]);
}

#[test]
fn a_forcecommand_changed_to_a_shell_is_drifted_on_that_directive() {
    // The exact failure the README describes: the jail's ForceCommand
    // replaced so the login becomes an ordinary shell session.
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand /bin/bash
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a ForceCommand pointed at a shell must not read as intact");
    };
    assert_eq!(missing, vec!["forcecommand internal-sftp".to_owned()]);
}

#[test]
fn a_chrootdirectory_removed_is_drifted_on_that_directive_alone() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block missing ChrootDirectory must not read as intact");
    };
    assert_eq!(missing, vec!["chrootdirectory %h".to_owned()]);
}

#[test]
fn allowtcpforwarding_turned_on_is_drifted() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding yes
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("AllowTcpForwarding turned on must not read as intact");
    };
    assert_eq!(missing, vec!["allowtcpforwarding no".to_owned()]);
}

#[test]
fn x11forwarding_turned_on_is_drifted() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding yes
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("X11Forwarding turned on must not read as intact");
    };
    assert_eq!(missing, vec!["x11forwarding no".to_owned()]);
}

#[test]
fn a_match_group_line_naming_a_different_group_is_drifted() {
    // The block is present and every directive is there, but it protects the
    // wrong group — which protects nobody the panel thinks it protects.
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group some-other-group
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block guarding the wrong group must not read as intact");
    };
    assert_eq!(missing, vec!["Match Group maran-sftp".to_owned()]);
}

#[test]
fn several_missing_directives_are_all_named() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block with only its opening line must not read as intact");
    };
    assert_eq!(
        missing,
        vec![
            "chrootdirectory %h".to_owned(),
            "authorizedkeysfile /etc/ssh/maran-sftp-keys/%u".to_owned(),
            "forcecommand internal-sftp".to_owned(),
            "allowtcpforwarding no".to_owned(),
            "x11forwarding no".to_owned(),
        ]
    );
}

/// **The planted break for the key directory.** Without `AuthorizedKeysFile`
/// the group falls back to the per-user `authorized_keys` in the customer's
/// own home, so a customer could add a key of their own and a suspended
/// login's parked key file would stop nothing; the old check read this as
/// intact because it never looked for the line.
#[test]
fn a_block_without_authorizedkeysfile_is_drifted_on_that_directive_alone() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("a block with no AuthorizedKeysFile must not read as intact");
    };
    assert_eq!(
        missing,
        vec!["authorizedkeysfile /etc/ssh/maran-sftp-keys/%u".to_owned()]
    );
}

/// The line is there but points at the home's own file: the override case.
#[test]
fn an_authorizedkeysfile_pointing_at_the_customers_home_is_drifted() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile .ssh/authorized_keys
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    let SftpJailStatus::Drifted { missing } = status else {
        panic!("an overridden AuthorizedKeysFile must not read as intact");
    };
    assert_eq!(
        missing,
        vec!["authorizedkeysfile /etc/ssh/maran-sftp-keys/%u".to_owned()]
    );
}

/// sshd keeps the FIRST value it obtains for a keyword, so a wrong line that
/// comes before the right one wins, and the right one after it must not rescue
/// the block.
#[test]
fn a_wrong_authorizedkeysfile_before_the_right_one_is_drifted() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile .ssh/authorized_keys
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    assert!(matches!(
        SftpJailStatus::evaluate(config, GROUP),
        SftpJailStatus::Drifted { .. }
    ));
}

/// The inverse control for the case above: the right line first, a harmless
/// repeat after it, is what sshd obeys and is still intact.
#[test]
fn the_right_authorizedkeysfile_first_is_intact_even_when_repeated() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group maran-sftp
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    AuthorizedKeysFile .ssh/authorized_keys
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    assert_eq!(
        SftpJailStatus::evaluate(config, GROUP),
        SftpJailStatus::Intact
    );
}

/// OpenSSH itself reads directive keywords without regard to case, so a
/// re-render in different case is not a drift this check should invent.
#[test]
fn directive_keywords_are_compared_case_insensitively() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
match group maran-sftp
    chrootdirectory %h
    authorizedkeysfile /etc/ssh/maran-sftp-keys/%u
    forcecommand internal-sftp
    allowtcpforwarding no
    x11forwarding no
# END Maran SFTP
";

    assert_eq!(
        SftpJailStatus::evaluate(config, GROUP),
        SftpJailStatus::Intact
    );
}

/// The group NAME, unlike a keyword, is a system identifier this product
/// controls — a case difference there is a different group, not a
/// formatting variation.
#[test]
fn the_group_name_itself_is_compared_case_sensitively() {
    let config = "\
# BEGIN Maran SFTP \u{2014} managed by installer/lib/86-sftp.sh, do not edit between markers
Match Group MARAN-SFTP
    ChrootDirectory %h
    AuthorizedKeysFile /etc/ssh/maran-sftp-keys/%u
    ForceCommand internal-sftp
    AllowTcpForwarding no
    X11Forwarding no
# END Maran SFTP
";

    let status = SftpJailStatus::evaluate(config, GROUP);

    assert!(matches!(status, SftpJailStatus::Drifted { .. }));
}
