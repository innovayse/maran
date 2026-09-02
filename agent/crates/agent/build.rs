//! Compiles the shared proto contract into Rust types at build time.
//!
//! The generated code is never committed (rules/proto.md): both sides of the
//! wire regenerate from `proto/`, so the contract cannot drift from a checked-in
//! copy that someone forgot to refresh.

fn main() -> Result<(), Box<dyn std::error::Error>> {
    // The client is generated too: the handshake test drives the server through
    // the same generated stub the C# side uses, rather than a hand-rolled caller
    // that could agree with a bug in the server.
    tonic_build::configure().build_client(true).compile_protos(
        &[
            "../../../proto/agent/v1/common.proto",
            "../../../proto/agent/v1/system.proto",
            "../../../proto/agent/v1/accounts.proto",
            "../../../proto/agent/v1/sites.proto",
            "../../../proto/agent/v1/ssl.proto",
            "../../../proto/agent/v1/php.proto",
            "../../../proto/agent/v1/files.proto",
            "../../../proto/agent/v1/db.proto",
            "../../../proto/agent/v1/ftp.proto",
        ],
        &["../../../proto"],
    )?;

    println!("cargo:rerun-if-changed=../../../proto");
    Ok(())
}
