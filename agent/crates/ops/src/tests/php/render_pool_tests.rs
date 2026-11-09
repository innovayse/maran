//! Tests for [`render_pool`].
//!
//! The renderer is shared by the pool writer and by the convergence of a site,
//! which compares its result with the file on disk. These pin that the two see
//! the same bytes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::php::fake_php_host::{FakePhpHost, distro, pool_input};
use crate::php::model::php_override::PhpOverride;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::render_pool::render_pool;
use crate::php::write_pool::write_pool_under_lock;

#[test]
fn the_rendered_pool_is_byte_identical_to_the_pool_the_writer_stores() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let input = pool_input(vec![PhpOverride::parse("memory_limit", "256M").unwrap()]);
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);

    let rendered = render_pool(distro(), &input, &paths).unwrap();
    write_pool_under_lock(&host, distro(), &input).unwrap();

    let stored = host
        .config(Path::new("/etc/php/8.3/fpm/pool.d/acme.conf"))
        .expect("the writer stored no pool");
    assert_eq!(rendered, stored);
}

#[test]
fn rendering_the_same_input_twice_gives_the_same_bytes() {
    let input = pool_input(Vec::new());
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);

    assert_eq!(
        render_pool(distro(), &input, &paths).unwrap(),
        render_pool(distro(), &input, &paths).unwrap()
    );
}

#[test]
fn a_pool_with_no_overrides_sets_both_size_limits_to_the_same_default() {
    let input = pool_input(Vec::new());
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);

    let rendered = render_pool(distro(), &input, &paths).unwrap();

    assert!(rendered.contains("php_value[upload_max_filesize] = 32M\n"));
    assert!(rendered.contains("php_value[post_max_size] = 32M\n"));
}

#[test]
fn an_override_follows_the_default_so_it_wins() {
    let input = pool_input(vec![
        PhpOverride::parse("upload_max_filesize", "64M").unwrap(),
        PhpOverride::parse("post_max_size", "64M").unwrap(),
    ]);
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);

    let rendered = render_pool(distro(), &input, &paths).unwrap();

    let last = |needle: &str| rendered.rfind(needle).unwrap();
    assert!(last("php_value[post_max_size] = 64M") > last("php_value[post_max_size] = 32M"));
    assert!(
        last("php_value[upload_max_filesize] = 64M") > last("php_value[upload_max_filesize] = 32M")
    );
}

#[test]
fn a_pool_whose_post_size_is_below_its_upload_size_is_not_rendered() {
    let input = pool_input(vec![
        PhpOverride::parse("upload_max_filesize", "64M").unwrap(),
    ]);
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);

    assert!(matches!(
        render_pool(distro(), &input, &paths),
        Err(crate::php::PhpOpError::OverridePostSizeBelowUpload { .. })
    ));
}
