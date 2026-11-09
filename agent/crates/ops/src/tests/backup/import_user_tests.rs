//! The temporary import user's name and password.

use super::*;

#[test]
fn a_user_name_is_the_prefix_and_sixteen_hex_digits() {
    let user = ImportUser::from_random(&[0x00, 0x01, 0x0a, 0xff, 0x10, 0x20, 0x30, 0x40]);

    assert_eq!(user.name(), "maran_imp_00010aff10203040");
}

#[test]
fn different_random_bytes_make_different_users() {
    let first = ImportUser::from_random(&[1; NAME_RANDOM_BYTES]);
    let second = ImportUser::from_random(&[2; NAME_RANDOM_BYTES]);

    assert_ne!(first, second);
}

#[test]
fn a_password_is_sixty_four_hex_digits_and_prints_redacted() {
    let password = ImportUser::password_from_random(&[0xab; PASSWORD_RANDOM_BYTES]);

    assert_eq!(password.expose(), "ab".repeat(PASSWORD_RANDOM_BYTES));
    assert!(!format!("{password:?}").contains("abab"));
}

#[test]
fn a_listed_name_built_by_the_loader_is_read_back_as_the_same_user() {
    let built = ImportUser::from_random(&[0x00, 0x01, 0x0a, 0xff, 0x10, 0x20, 0x30, 0x40]);

    assert_eq!(ImportUser::from_listed(built.name()), Some(built));
}

#[test]
fn a_listed_name_that_is_not_exactly_the_loaders_shape_is_not_one_of_ours() {
    for name in [
        "",
        "maran_imp_",
        "maran_imp_0001",
        "maran_imp_00010aff1020304",
        "maran_imp_00010aff102030405",
        "maran_imp_00010AFF10203040",
        "maran_imp_00010aff1020304g",
        "maran_imp_00010aff10203040 ",
        "maran_imp_00010aff10203040'",
        "xmaran_imp_00010aff10203040",
        "maran_impX00010aff10203040",
        "maran_imp_maran_imp_00010aff10203040",
        "alice_shop",
        "root",
    ] {
        assert_eq!(ImportUser::from_listed(name), None, "{name:?}");
    }
}
