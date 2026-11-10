//! The zone files the agent renders, read by the daemon's own zone checker.
//!
//! A golden proves the agent renders what it meant to; it cannot prove the daemon
//! accepts it. This suite hands every shape of rendered zone to the real
//! `named-checkzone` of the platform — the exact program `put_zone` runs as its
//! validator — so a template change that the checker would refuse fails here
//! instead of on a customer's first record.
//!
//! **Positive and negative control.** The refusal case feeds the same checker a
//! file with a malformed address and requires a non-zero status: a checker that
//! accepted everything (a stub, the wrong binary) would otherwise pass every
//! other test in this file.
//!
//! **UNOBSERVED HERE:** the daemon itself. Nothing here starts `named`, adds a
//! zone with `rndc` or asks it a question with `dig`; those need the polygon's
//! daemon fixture of the DNS daemon phase. What is observed is that the checker
//! accepts the bytes `put_zone` would swap in.
//!
//! The DNSSEC cases (issue #452) observe what can be observed WITHOUT a daemon,
//! with the platform's own tools: the key tag the agent computes is the one
//! `dnssec-keygen` names a real key with, the key files the agent's templates
//! render are read by `dnssec-signzone` and the DS record of the signed zone
//! carries the same tag, the zone statements and the policy are accepted by
//! `named-checkconf` (and a policy it does not know is refused), and a key file
//! the agent writes lands `root:<daemon group> 0640`. Each prints what it did NOT
//! observe; the list is [`state_the_blind_spots`].
//!
//! The suite needs `named-checkzone` and `named-checkconf` installed (`bind9-utils`
//! on the Debian family, `bind-utils` on the RHEL family) and, for the DNSSEC
//! cases, `dnssec-keygen`, `dnssec-signzone` and `dnssec-dsfromkey` (the same
//! package on the Debian family, `bind-dnssec-utils` on the RHEL family). Without
//! them every test fails loudly: a checker that was never found is not a checker
//! that passed.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::privs::group_id::GroupId;
use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::dns::caa_flags::CaaFlags;
use maran_agent_core::validation::dns::caa_tag::CaaTag;
use maran_agent_core::validation::dns::caa_value::CaaValue;
use maran_agent_core::validation::dns::dns_name::DnsName;
use maran_agent_core::validation::dns::dns_ttl::DnsTtl;
use maran_agent_core::validation::dns::dnssec_key_file_name::DnssecKeyFileName;
use maran_agent_core::validation::dns::dnssec_key_material::DnssecKeyMaterial;
use maran_agent_core::validation::dns::dnssec_key_material_error::DnssecKeyMaterialError;
use maran_agent_core::validation::dns::record_name::RecordName;
use maran_agent_core::validation::dns::record_priority::RecordPriority;
use maran_agent_core::validation::dns::soa_interval::SoaInterval;
use maran_agent_core::validation::dns::srv_weight::SrvWeight;
use maran_agent_core::validation::dns::txt_text::TxtText;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_agent_core::validation::dns::zone_serial::ZoneSerial;
use maran_agent_core::validation::web::port::Port;
use maran_distro::detect;
use maran_ops::dns::{
    ProcessDnsHost, RecordData, ZoneContent, ZoneHost, ZoneRecord, ZoneSoa, render_zone,
};
use maran_templates::bind::dnssec_private_key::DnssecPrivateKey;
use maran_templates::bind::dnssec_probe::DnssecProbe;
use maran_templates::bind::dnssec_public_key::DnssecPublicKey;
use maran_templates::bind::named_conf::NamedConf;
use maran_templates::bind::zone_statement::ZoneStatement;

/// The zone every test renders.
const ZONE: &str = "example.com";

fn name(text: &str) -> DnsName {
    DnsName::parse(text).unwrap()
}

fn record(owner: &str, ttl: u32, data: RecordData) -> ZoneRecord {
    ZoneRecord {
        name: if owner == "@" {
            RecordName::Apex
        } else {
            RecordName::parse(owner).unwrap()
        },
        ttl: DnsTtl::parse(ttl).unwrap(),
        data,
    }
}

/// A zone with one record of every type, a wildcard and a text long enough to be
/// split into two strings, with in-zone name servers that have addresses.
fn zone_with_every_type() -> ZoneContent {
    let soa = ZoneSoa {
        primary_nameserver: name("ns1.example.com"),
        hostmaster: name("hostmaster.example.com"),
        serial: ZoneSerial::parse(2_026_110_701).unwrap(),
        refresh: SoaInterval::parse(3600).unwrap(),
        retry: SoaInterval::parse(600).unwrap(),
        expire: SoaInterval::parse(1_209_600).unwrap(),
        negative_ttl: DnsTtl::parse(300).unwrap(),
    };
    let records = vec![
        record("ns1", 3600, RecordData::A("192.0.2.1".parse().unwrap())),
        record("ns2", 3600, RecordData::A("192.0.2.2".parse().unwrap())),
        record("@", 3600, RecordData::A("192.0.2.10".parse().unwrap())),
        record("@", 3600, RecordData::Aaaa("2001:db8::10".parse().unwrap())),
        record("www", 300, RecordData::Cname(name("example.com"))),
        record(
            "@",
            3600,
            RecordData::Mx {
                priority: RecordPriority::parse(10).unwrap(),
                exchange: name("mail.example.net"),
            },
        ),
        record(
            "@",
            3600,
            RecordData::Txt(TxtText::parse("v=spf1 mx ~all").unwrap()),
        ),
        record(
            "long",
            3600,
            RecordData::Txt(TxtText::parse(&"k".repeat(400)).unwrap()),
        ),
        record(
            "_dmarc",
            3600,
            RecordData::Txt(TxtText::parse("v=DMARC1; p=none").unwrap()),
        ),
        record(
            "_sip._tcp",
            3600,
            RecordData::Srv {
                priority: RecordPriority::parse(10).unwrap(),
                weight: SrvWeight::parse(20).unwrap(),
                port: Port::parse(5060).unwrap(),
                target: name("sip.example.net"),
            },
        ),
        record(
            "@",
            3600,
            RecordData::Caa {
                flags: CaaFlags::parse(0).unwrap(),
                tag: CaaTag::parse("issue").unwrap(),
                value: CaaValue::parse("letsencrypt.org").unwrap(),
            },
        ),
        record("sub", 3600, RecordData::Ns(name("ns.sub.example.net"))),
        record("*", 300, RecordData::A("192.0.2.11".parse().unwrap())),
    ];

    ZoneContent::new(
        ZoneName::parse(ZONE).unwrap(),
        soa,
        vec![name("ns1.example.com"), name("ns2.example.com")],
        records,
    )
    .unwrap()
}

/// Runs the platform's `named-checkzone` on `contents` and answers its exit
/// status and everything it printed.
fn check(contents: &str) -> (Option<i32>, String) {
    let distro = detect().expect("the polygon host is a supported system");
    let adapter = maran_distro::adapter_for(distro.family);
    let directory = tempfile::tempdir().unwrap();
    let file = directory.path().join(format!("{ZONE}.db"));
    std::fs::write(&file, contents).unwrap();

    let output = Command::new(adapter.dns_checkzone_binary().as_path())
        .arg("-q")
        .arg(ZONE)
        .arg(&file)
        .output()
        .expect("named-checkzone could not be started; is the BIND utilities package installed?");

    let printed = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    (output.status.code(), printed)
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn a_zone_with_every_record_type_is_accepted_by_the_real_checker() {
    let rendered = render_zone(&zone_with_every_type()).unwrap();

    let (status, printed) = check(&rendered);

    assert_eq!(
        status,
        Some(0),
        "named-checkzone refused:\n{printed}\n{rendered}"
    );
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn a_zone_with_no_records_but_its_apex_is_accepted() {
    let base = zone_with_every_type();
    let apex_only = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        vec![name("ns1.example.net")],
        vec![],
    )
    .unwrap();

    let (status, printed) = check(&render_zone(&apex_only).unwrap());

    assert_eq!(status, Some(0), "named-checkzone refused:\n{printed}");
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn the_checker_refuses_a_malformed_file_so_the_other_results_mean_something() {
    // The inverse control: the same checker, a file it must refuse.
    let rendered = render_zone(&zone_with_every_type())
        .unwrap()
        .replace("192.0.2.10", "192.0.2.300");

    let (status, _printed) = check(&rendered);

    assert_ne!(
        status,
        Some(0),
        "the checker accepted an impossible address"
    );
}

#[test]
#[ignore = "needs named-checkzone; runs in the polygon container"]
fn an_in_zone_name_server_without_an_address_is_refused_by_the_checker() {
    // Why the panel must send the glue: the checker refuses a delegation target
    // inside the zone that has no address record.
    let base = zone_with_every_type();
    let without_glue = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        base.nameservers().to_vec(),
        vec![],
    )
    .unwrap();

    let (status, _printed) = check(&render_zone(&without_glue).unwrap());

    assert_ne!(status, Some(0));
}

// ---------------------------------------------------------------------------
// DNSSEC (issue #452)
// ---------------------------------------------------------------------------

/// What no case below observes, printed by every case so a reader of the output
/// cannot take a green run for more than it is.
const BLIND_SPOTS: [&str; 7] = [
    "a running named adopting these key files under dnssec-policy (no daemon is started here)",
    "whether named adopts a key that has no .state file beside it",
    "rndc addzone / modzone / showzone / signing -list on this build, and the text they print",
    "a zone moving between the plain and the signed directory under a live daemon",
    "restart reconciliation of a signed zone (it needs the daemon's own zone list)",
    "AppArmor and SELinux permitting the daemon to write the signed-zone and key directories",
    "the size of a signed answer against an unsigned one (amplification)",
];

/// Prints [`BLIND_SPOTS`].
fn state_the_blind_spots() {
    for spot in BLIND_SPOTS {
        println!("UNOBSERVED HERE: {spot}");
    }
}

/// The absolute path of a BIND tool that no adapter fact names, looked for where
/// both families install them. A tool that is not there fails the case loudly.
fn tool(name: &str) -> PathBuf {
    ["/usr/bin", "/usr/sbin"]
        .iter()
        .map(|directory| Path::new(directory).join(name))
        .find(|path| path.exists())
        .unwrap_or_else(|| panic!("{name} is not installed; is the BIND utilities package there?"))
}

/// Runs `program` with `arguments` in `directory` and answers its exit status and
/// everything it printed.
fn run_in(directory: &Path, program: &Path, arguments: &[&str]) -> (Option<i32>, String) {
    let output = Command::new(program)
        .args(arguments)
        .current_dir(directory)
        .output()
        .unwrap_or_else(|error| panic!("{} could not be started: {error}", program.display()));

    let printed = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    (output.status.code(), printed)
}

/// A key the daemon's own tool generated: the parts the agent is handed.
struct GeneratedKey {
    /// The public key, standard base64 of `x || y`.
    public: String,
    /// The private scalar, standard base64.
    private: String,
    /// The tag `dnssec-keygen` named the key with.
    tag: u32,
}

/// Generates a combined signing key for `example.com` with `dnssec-keygen` into
/// `directory` and reads its two files back.
fn generate_key(directory: &Path) -> GeneratedKey {
    let (status, printed) = run_in(
        directory,
        &tool("dnssec-keygen"),
        &[
            "-q",
            "-a",
            "ECDSAP256SHA256",
            "-f",
            "KSK",
            "-n",
            "ZONE",
            ZONE,
        ],
    );
    assert_eq!(status, Some(0), "dnssec-keygen refused:\n{printed}");

    let base = printed.trim().to_owned();
    let tag: u32 = base
        .rsplit('+')
        .next()
        .and_then(|text| text.parse().ok())
        .unwrap_or_else(|| panic!("dnssec-keygen printed a name with no tag: {base:?}"));

    let public_file = std::fs::read_to_string(directory.join(format!("{base}.key"))).unwrap();
    let record = public_file
        .lines()
        .rev()
        .find(|line| !line.trim_start().starts_with(';') && line.contains("DNSKEY"))
        .expect("the key file holds a DNSKEY record");
    let words: Vec<&str> = record.split_whitespace().collect();
    let at = words.iter().position(|word| *word == "DNSKEY").unwrap();
    assert_eq!(words[at + 1], "257", "the generated key is not a 257 key");
    let public = words[at + 4..].concat();

    let private_file = std::fs::read_to_string(directory.join(format!("{base}.private"))).unwrap();
    let private = private_file
        .lines()
        .find_map(|line| line.strip_prefix("PrivateKey: "))
        .expect("the private file holds a PrivateKey line")
        .trim()
        .to_owned();

    GeneratedKey {
        public,
        private,
        tag,
    }
}

/// The agent's validated key for a generated one.
fn agent_key(generated: &GeneratedKey) -> DnssecKeyMaterial {
    DnssecKeyMaterial::parse(
        &generated.public,
        &SecretString::new(generated.private.clone()),
        257,
        generated.tag,
    )
    .expect("the agent refused a key the daemon's own tool generated")
}

#[test]
#[ignore = "needs dnssec-keygen; runs in the polygon container"]
fn the_key_tag_the_agent_computes_is_the_one_the_daemons_tool_names_the_key_with() {
    state_the_blind_spots();
    let directory = tempfile::tempdir().unwrap();
    let generated = generate_key(directory.path());

    // The accepting control: the agent's own computation agrees with the tool.
    let key = agent_key(&generated);
    assert_eq!(u32::from(key.key_tag()), generated.tag);

    // The planted failure: the same key with a tag that is off by one is refused,
    // so the agreement above is a comparison and not a copy.
    let refused = DnssecKeyMaterial::parse(
        &generated.public,
        &SecretString::new(generated.private.clone()),
        257,
        generated.tag + 1,
    );
    assert!(matches!(
        refused,
        Err(DnssecKeyMaterialError::KeyTagMismatch)
    ));
}

/// The apex-only zone, rendered.
fn apex_only_zone() -> String {
    let base = zone_with_every_type();
    let apex_only = ZoneContent::new(
        base.zone().clone(),
        base.soa().clone(),
        vec![name("ns1.example.net")],
        vec![],
    )
    .unwrap();
    render_zone(&apex_only).unwrap()
}

/// Renders the two key files of `key` with the agent's templates, as `put_zone`
/// writes them, into `directory`, under the names the agent derives.
fn write_agent_key_files(directory: &Path, key: &DnssecKeyMaterial) {
    let zone = ZoneName::parse(ZONE).unwrap();
    let name = DnssecKeyFileName::of(&zone, key).unwrap();
    let private = DnssecPrivateKey {
        algorithm: key.algorithm(),
        private_key: key.expose_private_key_base64(),
    }
    .render_config()
    .unwrap();
    let apex = zone.fully_qualified();
    let public = DnssecPublicKey {
        zone: &apex,
        key_tag: key.key_tag(),
        flags: key.flags(),
        protocol: key.protocol(),
        algorithm: key.algorithm(),
        public_key: key.public_key_base64(),
    }
    .render_config()
    .unwrap();

    std::fs::write(directory.join(name.private_file()), private).unwrap();
    std::fs::write(directory.join(name.public_file()), public).unwrap();
}

#[test]
#[ignore = "needs dnssec-signzone and dnssec-dsfromkey; runs in the polygon container"]
fn the_key_files_the_agent_renders_are_read_by_the_real_signer_and_carry_the_same_tag() {
    state_the_blind_spots();
    let generated_in = tempfile::tempdir().unwrap();
    let generated = generate_key(generated_in.path());
    let key = agent_key(&generated);

    let keys = tempfile::tempdir().unwrap();
    write_agent_key_files(keys.path(), &key);
    let work = tempfile::tempdir().unwrap();
    std::fs::write(work.path().join("zone.db"), apex_only_zone()).unwrap();

    // Smart signing (`-S`): the signer FINDS the zone's keys in the key directory by
    // the names the agent gave the files and reads their timing metadata, which is
    // how the daemon's tools meet a key. Naming the key on the command line instead
    // would need its DNSKEY already in the zone file, and would not look at the
    // file names at all.
    let key_directory = keys.path().to_str().unwrap();
    let (status, printed) = run_in(
        work.path(),
        &tool("dnssec-signzone"),
        &[
            "-q",
            "-S",
            "-z",
            "-K",
            key_directory,
            "-o",
            ZONE,
            "-f",
            "out.signed",
            "zone.db",
        ],
    );
    assert_eq!(
        status,
        Some(0),
        "dnssec-signzone refused the agent's key files:\n{printed}"
    );

    let (status, printed) = run_in(
        work.path(),
        &checker_for_zone(),
        &["-q", ZONE, "out.signed"],
    );
    assert_eq!(
        status,
        Some(0),
        "named-checkzone refused the signed zone:\n{printed}"
    );

    let (status, ds) = run_in(
        work.path(),
        &tool("dnssec-dsfromkey"),
        &["-f", "out.signed", ZONE],
    );
    assert_eq!(status, Some(0), "dnssec-dsfromkey refused:\n{ds}");
    let words: Vec<&str> = ds.split_whitespace().collect();
    let at = words.iter().position(|word| *word == "DS").unwrap();
    assert_eq!(
        words[at + 1].parse::<u16>().unwrap(),
        key.key_tag(),
        "the DS record names a different key than the panel computed: {ds}"
    );
}

#[test]
#[ignore = "needs dnssec-signzone; runs in the polygon container"]
fn the_signer_refuses_to_sign_without_the_key_files_so_the_case_above_means_something() {
    // The inverse control: the same signer, the same zone, an empty key directory.
    let empty = tempfile::tempdir().unwrap();
    let work = tempfile::tempdir().unwrap();
    std::fs::write(work.path().join("zone.db"), apex_only_zone()).unwrap();

    let (status, _printed) = run_in(
        work.path(),
        &tool("dnssec-signzone"),
        &[
            "-q",
            "-S",
            "-z",
            "-K",
            empty.path().to_str().unwrap(),
            "-o",
            ZONE,
            "-f",
            "out.signed",
            "zone.db",
        ],
    );

    assert_ne!(status, Some(0), "the signer signed with no key on disk");
}

/// The platform's `named-checkzone`.
fn checker_for_zone() -> PathBuf {
    let distro = detect().expect("the polygon host is a supported system");
    maran_distro::adapter_for(distro.family)
        .dns_checkzone_binary()
        .as_path()
        .to_path_buf()
}

/// The platform's `named-checkconf`.
fn checker_for_configuration() -> PathBuf {
    let distro = detect().expect("the polygon host is a supported system");
    maran_distro::adapter_for(distro.family)
        .dns_checkconf_binary()
        .as_path()
        .to_path_buf()
}

/// Runs `named-checkconf` on `text` written as a file of its own.
fn check_configuration(text: &str) -> (Option<i32>, String) {
    let directory = tempfile::tempdir().unwrap();
    let file = directory.path().join("named.conf");
    std::fs::write(&file, text).unwrap();

    run_in(
        directory.path(),
        &checker_for_configuration(),
        &[file.to_str().unwrap()],
    )
}

/// Whether this build's checker accepts the probe, as `probe_dnssec_support` asks.
fn build_can_sign() -> bool {
    let probe = DnssecProbe {}.render_config().unwrap();

    check_configuration(&probe).0 == Some(0)
}

#[test]
#[ignore = "needs named-checkconf; runs in the polygon container"]
fn the_zone_statements_are_accepted_by_the_real_checker_and_an_unknown_policy_is_not() {
    state_the_blind_spots();
    let policy = DnssecProbe {}.render_config().unwrap();
    let plain = ZoneStatement::unsigned("/z/example.com.db")
        .statement()
        .unwrap();
    let signed = ZoneStatement::signed("/s/example.com.db", "/k")
        .statement()
        .unwrap();
    let withdrawing = ZoneStatement::withdrawing("/s/example.com.db", "/k")
        .statement()
        .unwrap();
    let configuration = |body: &str| format!("{policy}zone \"{ZONE}\" {body}\n");
    let without_policy = |body: &str| format!("zone \"{ZONE}\" {body}\n");

    // The control that holds on every build: the statement that predates signing,
    // in a configuration that states no policy.
    let (status, printed) = check_configuration(&without_policy(&plain));
    assert_eq!(
        status,
        Some(0),
        "the unsigned statement was refused:\n{printed}"
    );

    if build_can_sign() {
        for (label, statement) in [("signed", &signed), ("withdrawing", &withdrawing)] {
            let (status, printed) = check_configuration(&configuration(statement));
            assert_eq!(
                status,
                Some(0),
                "the {label} statement was refused:\n{printed}"
            );
        }

        // The planted failure: a policy nobody defined is refused, so the
        // acceptances above are the checker reading the policy name.
        let undefined = signed.replace("maran-csk", "maran-undefined");
        let (status, _printed) = check_configuration(&configuration(&undefined));
        assert_ne!(status, Some(0), "the checker accepted an undefined policy");
    } else {
        println!("UNOBSERVED HERE: this build has no dnssec-policy, so signing is hidden");
        let (status, _printed) = check_configuration(&configuration(&signed));
        assert_ne!(status, Some(0), "a build without dnssec-policy accepted it");
    }
}

#[test]
#[ignore = "needs named-checkconf; runs in the polygon container"]
fn the_rendered_named_conf_states_the_policy_exactly_when_this_build_accepts_one() {
    state_the_blind_spots();
    let directory = tempfile::tempdir().unwrap();
    let key = directory.path().join("rndc.key");
    std::fs::write(
        &key,
        "key \"maran-rndc\" {\n    algorithm hmac-sha256;\n    secret \"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\";\n};\n",
    )
    .unwrap();
    let render = |supported: bool| {
        NamedConf {
            control_key_path: key.to_str().unwrap().to_owned(),
            control_key_name: "maran-rndc".to_owned(),
            control_port: 953,
            state_directory: directory.path().to_str().unwrap().to_owned(),
            dnssec_policy_supported: supported,
        }
        .render_config()
        .unwrap()
    };

    // The configuration without the statement is accepted by every build.
    let (status, printed) = check_configuration(&render(false));
    assert_eq!(
        status,
        Some(0),
        "the plain configuration was refused:\n{printed}"
    );

    // The configuration with it is accepted exactly when the probe was: the
    // reason the statement cannot be rendered unconditionally.
    let (with_policy, printed) = check_configuration(&render(true));
    if build_can_sign() {
        assert_eq!(with_policy, Some(0), "refused although probed:\n{printed}");
    } else {
        assert_ne!(
            with_policy,
            Some(0),
            "accepted although the probe was refused"
        );
    }
}

#[test]
#[ignore = "needs root and the daemon's group; runs in the polygon container"]
fn a_key_file_the_agent_writes_is_root_owned_0640_and_in_the_daemons_group() {
    state_the_blind_spots();
    let distro = detect().expect("the polygon host is a supported system");
    let adapter = maran_distro::adapter_for(distro.family);
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("Kexample.com.+013+12345.private");

    ProcessDnsHost::new()
        .write_key_file(&target, "Private-key-format: v1.3\n", adapter.dns_group())
        .unwrap();

    let metadata = std::fs::metadata(&target).unwrap();
    assert_eq!(metadata.mode() & 0o7777, 0o640);
    assert_eq!(metadata.uid(), 0);
    assert_eq!(
        metadata.gid(),
        GroupId::resolve(adapter.dns_group()).unwrap().gid(),
        "the key file is not in the daemon's group"
    );
    assert_eq!(
        std::fs::read_to_string(&target).unwrap(),
        "Private-key-format: v1.3\n"
    );
    let entries: Vec<_> = std::fs::read_dir(directory.path()).unwrap().collect();
    assert_eq!(entries.len(), 1, "a temporary file was left beside the key");
}

#[test]
#[ignore = "needs root; runs in the polygon container"]
fn a_key_file_for_a_group_that_does_not_exist_is_refused_and_leaves_nothing() {
    // The inverse control of the case above: the same writer, an unresolvable
    // group. Nothing may be on disk afterwards.
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("Kexample.com.+013+12345.private");

    let refused = ProcessDnsHost::new().write_key_file(
        &target,
        "Private-key-format: v1.3\n",
        "no-such-group-for-maran",
    );

    assert!(refused.is_err());
    assert_eq!(std::fs::read_dir(directory.path()).unwrap().count(), 0);
}
