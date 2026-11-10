//! What a `StreamRemoteDatabase` request must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_dump_request;
use crate::proto::{self, ErrorCode, StreamRemoteDatabaseRequest};

/// An inert key document that opens nothing anywhere.
const KEY: &str = "-----BEGIN OPENSSH PRIVATE KEY-----\n\
    b3BlbnNzaC1rZXktdjEACQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ\n\
    -----END OPENSSH PRIVATE KEY-----\n";

/// An inert host key.
const HOST_KEY: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

/// The agent's own ceiling, as the module states it.
const CEILING: u64 = 32 * 1024 * 1024 * 1024;

/// A request every field of which is valid; each test breaks one.
fn valid() -> StreamRemoteDatabaseRequest {
    StreamRemoteDatabaseRequest {
        endpoint: Some(proto::SourceEndpoint {
            host: "192.0.2.10".to_owned(),
            port: 22,
            user: "root".to_owned(),
        }),
        credential: Some(proto::SourceCredential {
            private_key: KEY.as_bytes().to_vec(),
            host_key: HOST_KEY.to_owned(),
        }),
        source_database: "shop_wp".to_owned(),
        account_username: "alice".to_owned(),
        target_database: "alice_shop".to_owned(),
        max_bytes: 0,
    }
}

#[test]
fn a_valid_request_is_yielded_with_the_ceiling_for_zero() {
    let dump = validated_dump_request(valid()).unwrap();

    assert_eq!(dump.source_database.as_str(), "shop_wp");
    assert_eq!(dump.target_database.as_str(), "alice_shop");
    assert_eq!(dump.max_bytes, CEILING);
}

#[test]
fn the_smaller_of_the_request_and_the_ceiling_applies() {
    let mut small = valid();
    small.max_bytes = 1024;
    let mut huge = valid();
    huge.max_bytes = u64::MAX;

    assert_eq!(validated_dump_request(small).unwrap().max_bytes, 1024);
    assert_eq!(validated_dump_request(huge).unwrap().max_bytes, CEILING);
}

#[test]
fn a_target_that_is_not_a_database_of_the_account_is_refused() {
    for target in ["bob_shop", "mysql", "alice", "alice_shop_x_y", ""] {
        let mut request = valid();
        request.target_database = target.to_owned();

        let failure = validated_dump_request(request).unwrap_err();

        assert_eq!(
            failure.error.code,
            ErrorCode::InvalidInput as i32,
            "{target:?}"
        );
    }
}

#[test]
fn a_source_database_name_a_shell_could_read_is_refused() {
    for name in ["a b", "a'b", "a;b", "a`b", "", "a-b"] {
        let mut request = valid();
        request.source_database = name.to_owned();

        let failure = validated_dump_request(request).unwrap_err();

        assert_eq!(
            failure.error.code,
            ErrorCode::InvalidInput as i32,
            "{name:?}"
        );
    }
}
