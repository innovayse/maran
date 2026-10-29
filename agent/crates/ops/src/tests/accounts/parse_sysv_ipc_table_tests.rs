//! Tests for [`parse_sysv_ipc_table`]: the header-driven reader of the
//! `/proc/sysvipc` tables.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::accounts::AccountError;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::model::sysv_ipc_object::SysvIpcObject;
use crate::accounts::parse_sysv_ipc_table::parse_sysv_ipc_table;

/// A real-shaped shared-memory table with two segments.
const SHM: &str = "       key      shmid perms                  size  cpid  lpid nattch   uid   gid  cuid  cgid      atime      dtime      ctime                   rss                  swap\n\
         0          5   600                  4096   100   100      0  1001  1001  1002  1002          0          0 1700000000                     0                     0\n\
         0          6   600                  4096   100   100      0     0     0     0     0          0          0 1700000000                     0                     0\n";

/// A message-queue table with one queue.
const MSG: &str = "       key      msqid perms      cbytes       qnum lspid lrpid   uid   gid  cuid  cgid      stime      rtime      ctime\n\
         0          9   644           0          0     0     0  1001  1001  1001  1001          0          0 1700000000\n";

/// A semaphore table with one set.
const SEM: &str = "       key      semid perms      nsems   uid   gid  cuid  cgid      otime      ctime\n\
         0          3   600          1  1001  1001  1001  1001          0 1700000000\n";

#[test]
fn a_shared_memory_table_yields_id_owner_and_creator() {
    let objects = parse_sysv_ipc_table(SysvIpcKind::Shm, SHM).expect("parses");

    assert_eq!(
        objects,
        vec![
            SysvIpcObject {
                kind: SysvIpcKind::Shm,
                id: 5,
                uid: 1001,
                cuid: 1002
            },
            SysvIpcObject {
                kind: SysvIpcKind::Shm,
                id: 6,
                uid: 0,
                cuid: 0
            },
        ]
    );
}

#[test]
fn a_message_queue_table_is_read_by_its_msqid_column() {
    let objects = parse_sysv_ipc_table(SysvIpcKind::Msg, MSG).expect("parses");

    assert_eq!(
        objects,
        vec![SysvIpcObject {
            kind: SysvIpcKind::Msg,
            id: 9,
            uid: 1001,
            cuid: 1001
        }]
    );
}

#[test]
fn a_semaphore_table_is_read_by_its_semid_column() {
    let objects = parse_sysv_ipc_table(SysvIpcKind::Sem, SEM).expect("parses");

    assert_eq!(
        objects,
        vec![SysvIpcObject {
            kind: SysvIpcKind::Sem,
            id: 3,
            uid: 1001,
            cuid: 1001
        }]
    );
}

#[test]
fn columns_are_found_by_name_when_the_order_differs() {
    let text = "cuid uid semid\n7 8 9\n";

    let objects = parse_sysv_ipc_table(SysvIpcKind::Sem, text).expect("parses");

    assert_eq!(
        objects,
        vec![SysvIpcObject {
            kind: SysvIpcKind::Sem,
            id: 9,
            uid: 8,
            cuid: 7
        }]
    );
}

#[test]
fn a_header_only_table_is_empty() {
    let header = SEM.lines().next().unwrap();

    assert!(
        parse_sysv_ipc_table(SysvIpcKind::Sem, header)
            .unwrap()
            .is_empty()
    );
}

#[test]
fn an_empty_text_is_an_empty_table() {
    assert!(
        parse_sysv_ipc_table(SysvIpcKind::Shm, "")
            .unwrap()
            .is_empty()
    );
    assert!(
        parse_sysv_ipc_table(SysvIpcKind::Shm, "\n  \n")
            .unwrap()
            .is_empty()
    );
}

#[test]
fn a_header_without_the_cuid_column_is_unreadable() {
    let error = parse_sysv_ipc_table(SysvIpcKind::Shm, "shmid uid\n1 2\n").unwrap_err();

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

#[test]
fn a_header_of_another_kind_is_unreadable() {
    let error = parse_sysv_ipc_table(SysvIpcKind::Msg, SEM).unwrap_err();

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

#[test]
fn a_row_with_a_non_numeric_value_is_unreadable() {
    let text = "semid uid cuid\n1 root 3\n";

    let error = parse_sysv_ipc_table(SysvIpcKind::Sem, text).unwrap_err();

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}

#[test]
fn a_short_row_is_unreadable() {
    let text = "semid uid cuid\n1 2\n";

    let error = parse_sysv_ipc_table(SysvIpcKind::Sem, text).unwrap_err();

    assert!(matches!(error, AccountError::UnreadableOutput { .. }));
}
