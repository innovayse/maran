//! Test support, not a test: self-signed certificates whose validity, subject and key usage the
//! test states, so a verifier is driven with exactly the certificate each case is about.
//!
//! One definition for the unit tests of `fleet/` and the integration test of the listener (which
//! mounts this file by path), because a certificate built by hand in each would be two fixtures
//! that drift. Everything is generated for the test and discarded with it; nothing here is real
//! material.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
#![allow(dead_code, missing_docs)]

use rcgen::{
    CertificateParams, DistinguishedName, DnType, ExtendedKeyUsagePurpose, KeyPair,
    PKCS_ECDSA_P256_SHA256, SerialNumber, date_time_ymd,
};

/// 2027-06-01 UTC, as seconds since the epoch: inside the validity of every certificate here
/// except [`expired_client_certificate`], which ended in 2021. The verifier is handed this
/// instead of the clock, so no test depends on today's date.
pub const TEST_NOW_SECONDS: u64 = 1_811_808_000;

/// A self-signed certificate and the key pair that signed it.
pub(crate) struct TestCertificate {
    /// The certificate, DER.
    pub der: Vec<u8>,
    /// The certificate as PEM text.
    pub pem: String,
    /// The key pair; the same key signs a re-issued certificate.
    pub key_pair: KeyPair,
    /// The subject common name, kept so a re-issue can state the same subject.
    pub common_name: String,
    /// The extended key usage the certificate carries.
    usage: ExtendedKeyUsagePurpose,
}

impl TestCertificate {
    /// The private key as PKCS#8 PEM text.
    pub fn key_pem(&self) -> String {
        self.key_pair.serialize_pem()
    }

    /// The private key, PKCS#8 DER.
    pub fn key_der(&self) -> Vec<u8> {
        self.key_pair.serialize_der()
    }

    /// The SAME key pair certified again: same subject, a different serial number and a
    /// different validity start, so a different DER. A chain check over the original accepts
    /// it, since subject and public key are all that check compares; only a fingerprint
    /// comparison tells the two apart.
    pub fn reissued(&self) -> Self {
        issue(
            &self.common_name,
            self.usage.clone(),
            KeyPair::from_pem(&self.key_pem()).unwrap(),
            2,
            (2026, 6, 1),
            (2036, 1, 1),
        )
    }
}

/// A client certificate valid from 2026-01-01 to 2036-01-01 with the `clientAuth` usage, on a
/// fresh key.
pub(crate) fn valid_client_certificate(common_name: &str) -> TestCertificate {
    issue(
        common_name,
        ExtendedKeyUsagePurpose::ClientAuth,
        new_key_pair(),
        1,
        (2026, 1, 1),
        (2036, 1, 1),
    )
}

/// A client certificate that ended in 2021, on a fresh key.
pub(crate) fn expired_client_certificate(common_name: &str) -> TestCertificate {
    issue(
        common_name,
        ExtendedKeyUsagePurpose::ClientAuth,
        new_key_pair(),
        1,
        (2020, 1, 1),
        (2021, 1, 1),
    )
}

/// A server certificate for the host name `localhost`, valid 2026-01-01 to 2036-01-01 with the
/// `serverAuth` usage, on a fresh key: what a test node presents.
pub(crate) fn valid_server_certificate() -> TestCertificate {
    let key_pair = new_key_pair();
    let mut params = CertificateParams::new(vec!["localhost".to_owned()]).unwrap();
    fill(
        &mut params,
        "test-node",
        ExtendedKeyUsagePurpose::ServerAuth,
        1,
        (2026, 1, 1),
        (2036, 1, 1),
    );
    let certificate = params.self_signed(&key_pair).unwrap();
    TestCertificate {
        der: certificate.der().to_vec(),
        pem: certificate.pem(),
        key_pair,
        common_name: "test-node".to_owned(),
        usage: ExtendedKeyUsagePurpose::ServerAuth,
    }
}

/// A fresh ECDSA P-256 key pair, the kind the product uses.
fn new_key_pair() -> KeyPair {
    KeyPair::generate_for(&PKCS_ECDSA_P256_SHA256).unwrap()
}

/// Issues one self-signed certificate over `key_pair`.
fn issue(
    common_name: &str,
    usage: ExtendedKeyUsagePurpose,
    key_pair: KeyPair,
    serial: u64,
    not_before: (i32, u8, u8),
    not_after: (i32, u8, u8),
) -> TestCertificate {
    let mut params = CertificateParams::new(Vec::<String>::new()).unwrap();
    fill(
        &mut params,
        common_name,
        usage.clone(),
        serial,
        not_before,
        not_after,
    );
    let certificate = params.self_signed(&key_pair).unwrap();
    TestCertificate {
        der: certificate.der().to_vec(),
        pem: certificate.pem(),
        key_pair,
        common_name: common_name.to_owned(),
        usage,
    }
}

/// Sets every field the tests care about; rcgen's defaults (a 1975 start, a 4096 end, one
/// constant subject) are wrong for all of them.
fn fill(
    params: &mut CertificateParams,
    common_name: &str,
    usage: ExtendedKeyUsagePurpose,
    serial: u64,
    not_before: (i32, u8, u8),
    not_after: (i32, u8, u8),
) {
    let mut subject = DistinguishedName::new();
    subject.push(DnType::CommonName, common_name);
    params.distinguished_name = subject;
    params.extended_key_usages = vec![usage];
    params.serial_number = Some(SerialNumber::from(serial));
    params.not_before = date_time_ymd(not_before.0, not_before.1, not_before.2);
    params.not_after = date_time_ymd(not_after.0, not_after.1, not_after.2);
}
