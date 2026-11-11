//! The Postfix uid read out of the user database.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::postfix_uid_from_passwd::postfix_uid_from_passwd;

#[test]
fn the_postfix_uid_is_read_from_its_passwd_line() {
    let passwd =
        "root:x:0:0::/root:/bin/bash\npostfix:x:105:107::/var/spool/postfix:/usr/sbin/nologin\n";
    assert_eq!(postfix_uid_from_passwd(passwd, "postfix").unwrap(), 105);
}

#[test]
fn a_host_without_the_postfix_user_fails_the_render_input() {
    let passwd = "root:x:0:0::/root:/bin/bash\npostfixer:x:9:9::/:/bin/false\n";
    assert!(postfix_uid_from_passwd(passwd, "postfix").is_err());
    assert!(postfix_uid_from_passwd("postfix:x:abc:1::/:/bin/false\n", "postfix").is_err());
}
