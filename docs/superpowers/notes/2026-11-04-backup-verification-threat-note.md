# Backup verification (issue #134) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change adds an rpc through which the panel makes the only root process on the server read a
root-owned artifact whose CONTENT is customer-derived: `BackupService.VerifyBackup` (agent contract
revision 17 to 18, additive). It also adds panel-side state, an endpoint and an alert mail that
report what that read found. This note is written **before any of the code exists**, as
rules/security.md requires, so every statement below is a REQUIREMENT the code must satisfy and not
a description of code. When the code lands, the author of the code does not edit this note to match
it; a deviation is recorded in a separate "Deviations found when the code landed" section added
afterwards, stated plainly. The branch may land on `fix/134-backup-verification` but MUST NOT merge
to `main` until a second reviewer has read this note and the diff and checked the items under "What
a reviewer must check". An agent session wrote this note and cannot supply that reviewer; this note
records the debt and discharges none of it.

## The surface

1. A new rpc, `BackupService.VerifyBackup(VerifyBackupRequest) returns (VerifyBackupResponse)`.
   Request: `account_username`, `backup_id`, `destination` (a `BackupDestination`). Response: a
   oneof of `VerifyBackupOk { member_count, database_count, artifact_bytes }` and `AgentError`.
   Only the LOCAL destination is served; any other kind answers `ERROR_CODE_NOT_IMPLEMENTED`
   (the remote half is declared and unwired; verification of a remote target belongs to #160).
2. The agent reads, as root, the artifact `<backup root>/<account>/<backup_id>.tar.gz` and its
   sidecar. It composes pieces that already exist and adds no new way to read a customer file:
   `read_sidecar`, `checksum_file`, `read_manifest`, `scan_members`, `require_sidecar_agrees`. It
   extracts nothing into the account's home and starts no program that a restore does not already
   start. It is read-only and idempotent: repeating it changes nothing on the host.
3. The panel exposes `POST api/v1/backups/{id}/verify`, records the result on the backup row
   (`BackupVerificationState` `NotVerified`, `Verified`, `Failed`; `VerifiedAt`;
   `VerificationFailureCode`), journals `BackupVerified` / `BackupVerificationFailed`, and runs the
   same verification after EVERY completed run, scheduled or manual. A verification failure mails
   the administrator through the shared operator-alert seam.
4. No new listening port, daemon, outbound call or rpc that executes caller-supplied input
   (rules/security.md items 10 and 12). The request carries two identifiers and a destination
   descriptor, never a path, a command or a template.

## What an attacker could try, and what each requirement is

Who can reach it. The agent serves one peer-credentialed unix socket, so every request comes from
the panel's uid: the actors are a compromised panel process (can call any rpc with any argument), a
panel administrator, and a hosting customer reaching only the panel's HTTP route. The artifact's
CONTENT is a fourth, indirect actor: a customer controls everything inside their own home, and the
home is what the archive holds, so the archive is hostile input even though the file is root-owned.
Someone who can already write root-owned files is out of scope: that person already has root.

### A. Hostile archive content

R-A1. **The archive is read, never unpacked, by verification.** The member scan (`tar --list`,
through `list_archive_members`) is the only thing that reads the home part. Verification MUST NOT
extract `home/` anywhere, as root or as the account. The extraction of `manifest.json` into the
root-only scratch is the one read of member CONTENT that is allowed, because it is the same read a
restore performs first and under the same discipline. If dump digests are checked (R-A5) the dump
members are the second.

R-A2. **Symlink and hardlink members, and device and fifo members.** A customer can put a symlink
or a hardlink in their home that points at `/etc/shadow` or `/root`. The scan reads names and, as it
stands, the layout rule decides on names (`manifest.json`, `home`, `databases`, anything under
`home/` or `databases/`). Verification MUST apply exactly the pre-scan a restore applies, to the
WHOLE listing, and a refusal MUST fail the verification entire (never skip the member and report
success). Because nothing is extracted, a link member inside `home/` is inert for verification; it
is the restore's belt (`tar` stripping and the account-side extraction) that holds the rest. A link
member whose NAME is `manifest.json` or begins with `databases/` is the dangerous case: it is a
member whose content is read by root. Verification MUST refuse a manifest or dump member that is not
a regular file, at the point of reading it, before any byte of it is followed. The reviewer checks
that this refusal is present and tested, because the layout scan alone does not catch it.

R-A3. **Decompression bombs.** The artifact is gzip. Verification MUST NOT decompress to memory or
to a file of unbounded size. Every `tar` run goes through `run_bounded` with a `CallClass` (a
deadline that means "wedged", never "slow"), with the capped drain, and with a typed `Bound` error:
a bound is a REFUSED verification, never a truncated listing handed on. The existing bounds
(listing bytes cap, member count cap, member name length) are reused unchanged; verification MUST
NOT raise them and MUST NOT carry a bound of its own that differs from restore's, because a backup
that verifies but cannot be restored (or the reverse) is a lie told in either direction. Hashing the
artifact is streamed in fixed chunks (`checksum_file`), never read whole.

R-A4. **Manifest as hostile JSON.** `manifest.json` is customer-derived only indirectly (the agent
wrote it), but an artifact on disk can be replaced. It is parsed by the existing `read_manifest`,
which refuses an unknown version and a manifest naming another account. Its size MUST be bounded
before parse (the extraction into scratch is bounded by the same `tar` run bounds; a manifest member
larger than a stated ceiling is refused), and a database name read from it MUST be re-validated by
`DatabaseName` before it is used for anything, including as a path component.

R-A5. **Dump digests, and the boundary of "dump parse".** The issue asks for a dump parse. Verification
MUST NOT parse, load, interpret or execute SQL: a dump is text that the loader runs as the database
superuser, and a root reader that "parses" it is a second, unreviewed SQL front end. What is
required is the existing check only: the manifest's recorded SHA-256 of each dump equals the digest
of the dump member as read from the artifact, and the dump member is present. If the existing archive
code cannot compute a member's digest without writing it to disk, then either the member is
extracted into the root-only scratch under `root_only_chain` discipline, with the free-space floor
asked first and the scratch reaped on every path (as restore does), or the digest is not checked and
the limit is stated in the rpc's doc comment and in docs/backups/verification.md. It MUST NOT be
silently omitted, and it MUST NOT be checked by a looser means. Whichever way, `VerifyBackupOk`
carries no claim the code did not establish: `database_count` is the count of dumps whose presence
(and digest, if checked) was established.

### B. Sidecar tampering

R-B1. The sidecar is a label, the artifact is the thing. Verification MUST compute the artifact's
SHA-256 itself and compare it with the sidecar's `artifact_sha256`, and compare the sidecar's size
with the measured size. A sidecar that is missing, unparsable, of an unknown version or that
describes itself as unreadable is a typed failure (`read_sidecar` already refuses these), never an
empty ok.

R-B2. **A sidecar and an artifact that were both replaced agree with each other and prove nothing.**
`require_sidecar_agrees` compares the sidecar with the manifest INSIDE the archive; an attacker who
can write both can make them agree. This is a stated limit, not a control: verification detects
corruption and accidental damage and the cases where only one of the two was changed. It does not
detect a coordinated replacement by someone who can write root-owned files, and a reviewer MUST NOT
read a green verification as that. The docs say it (R-H1).

R-B3. The digest is compared in a way that does not matter for timing (these are not secrets), but
both values MUST be validated as 64 lowercase hex characters before comparison, so that a malformed
value is a corrupt sidecar and not a mismatch of an unspecified kind.

### C. Path containment

R-C1. The artifact path is never taken from the request. It is derived from the validated
`LocalBackupRoot` (the agent's own configured root), the validated `AccountName` and the validated
`BackupId`, through the same helpers a restore and a delete use (`object_key`'s file-name functions;
`open_account_directory_owned_by`, which applies the inode checks to the root and to the account's
directory). The account's directory is opened by descriptor with ownership and mode checked and
symlinks refused; the artifact and sidecar are opened relative to that descriptor with
`O_NOFOLLOW`, and refused unless they are regular files with the expected owner and a single link.
A string comparison on a path is not containment (rules/security.md item 2).

R-C2. `BackupId` is validated by its constructor (the existing `backup_id` type) before any path is
formed. A request whose `backup_id` carries a separator, a `..`, a NUL or a control character is
`ERROR_CODE_INVALID_INPUT` and nothing is opened.

R-C3. A directory or a symbolic link wearing the artifact's name is neither verified nor followed;
it is the "not a file that could have a sidecar" case `list_backups` already names, and is a typed
failure.

R-C4. A TOCTOU between the listing and the read is closed by reading from the descriptor that was
opened and checked, and by taking the account's backup lock so that a concurrent delete or restore
cannot replace the artifact under the reader (see R-D2).

### D. Resource bounds

R-D1. Verification is a root process doing bulk I/O on a customer-sized artifact (tens of GiB). Every
run is bounded in time (`CallClass`), output (capped drain) and memory (streaming hash, no
whole-file read). The rpc is **unary**, not streaming, and MUST state in its doc comment its
cancellation class: it is read-only, so a dropped caller stops the work where the blocking loop can
ask whether the caller is still there, or it runs to completion and its result is discarded; the doc
comment says which and why (rules/rust.md "Async and blocking"). Which one is a decision for the
code; that it is written down is a requirement.

R-D2. Verification and the other backup operations contend. It MUST take the account's backup lock
the way delete does (so it never reads an artifact a restore is replacing or a delete is removing),
and MUST refuse, never wait, with `AlreadyRunning`, because a verification after a run is unattended
work and a wait held by minutes-long work stalls the panel (rules/rust.md "What this agent
serialises"). A refusal is a typed outcome the panel records as "could not verify now", NOT as a
failed artifact: see R-G2.

R-D3. The panel can issue this rpc once per completed run and on every click of "Verify now" by an
account or administrator. The agent's saturation limit (`ERROR_CODE_AGENT_SATURATED`) and the
backup lock bound how many run at once; the panel MUST NOT queue verifications without bound (one per
backup at a time) and MUST NOT run verification on the request thread of the scheduler's run
handler in a way that makes a hung verification hold the next run's slot. The reviewer judges
whether the bound is sufficient; the author could not measure it (below).

R-D4. If scratch is used (R-A5), the free-space floor is asked before the first write, the scratch is
root-only with the inode discipline restore uses, and it is removed on every exit path including a
bound and a refusal, and a stale one from a crash is reaped by the existing reaper.

### E. A read oracle for another account's backup

The rpc names an `account_username` and a `backup_id`. A compromised panel (or a panel bug) that
passes the wrong account would make root read another account's artifact and report on it. Two
independent controls are required; neither is the other's substitute.

R-E1. **The panel scopes.** The verify endpoint resolves the backup through the tenant-filtered
query, so a backup belonging to another account is `404` (`ErrorType.NotFound`), never `403`
(rules/security.md item 6), and the `account_username` and `backup_id` it hands the agent are read
from THAT row, never from the request. The endpoint carries `[ViewAsAccess]` and is refused while
viewing as, exactly as the neighbouring restore and delete endpoints are; the reviewer reads those
two to confirm the same scoping. An IDOR test proves a second account's login gets 404 for the first
account's backup id, and that nothing reached the agent.

R-E2. **The agent re-validates account against backup.** The artifact is found only under
`<root>/<account>/`, so a `backup_id` that exists under another account does not resolve (NotFound).
The manifest's `account` MUST equal `account_username` (`read_manifest` already refuses a mismatch:
`ManifestAccountMismatch`) and the sidecar's own recorded account, if it carries one, likewise. The
manifest's `backup_id` MUST equal the requested `backup_id`: an artifact copied or renamed under
another id is a refusal, not a verification of whatever it contains. Because the response reports
counts and a size, a mismatch that were NOT refused would let a caller learn that another account's
artifact exists and how big it is.

R-E3. NotFound for "no such backup under this account" and the refusal for "the artifact is not
this account's" MUST NOT be distinguishable to the panel's caller beyond what the panel already
shows its own administrator (see F).

### F. Information in errors

R-F1. A verification failure carries a typed code, never text from the artifact. Names that came out
of an archive are untrusted: where one must appear in an operator-facing message it goes through the
existing `refused_member_name` escaping and truncation (a newline in a name splits a log line, the
same mechanism as rules/security.md item 4). A tool's captured output is not forwarded.

R-F2. The panel stores only a code in `VerificationFailureCode` (a closed set the panel maps to
localized wording), never the agent's message, a path, a member name, a digest, a version or a tool
output. `BackupDto` carries the code and a backend-localized message, as the run failure does, so
the SPA holds no domain wording (rules/architecture.md "The backend owns the data").

R-F3. A customer-facing response states the verification state and the failure KIND (for example
"the artifact does not match its record") and nothing about where, which member or which digest.
Role-aware mapping applies: an administrator may be shown more than the owning account's login, and
the stronger wording is chosen on the server.

R-F4. The agent never logs artifact content, a dump, a manifest body or a full member name; the
failure is logged once at the boundary with the typed variant (rules/rust.md "Logging").

### G. State, and what the failure means

R-G1. **Failure is a domain outcome.** A corrupt, missing or changed artifact answers
`ERROR_CODE_VALIDATION_FAILED` (or a dedicated code only if the enum is extended in the same change
and the baseline accepted); a host that could not do the read answers `ERROR_CODE_SYSTEM_FAILURE`.
The two MUST stay apart, because the panel treats them differently (R-G2). An ok is only ever
produced after every check above ran to completion; an `ok` that carries zeros because something was
not read is a defect of the severity of the behaviour it hides.

R-G2. **A verification that could not run is not a verification that failed.** A lock refusal, a
saturated agent, a bound reached on a host that is merely slow, an agent that is unreachable: each
leaves the backup `NotVerified` (or its previous state) and MUST NOT mark it `Failed` and MUST NOT
mail "your backup is corrupt". Only a typed finding about the artifact marks `Failed`. Conversely a
verification that the agent could not perform MUST NOT be recorded as `Verified`.

R-G3. **A failed verification never rewrites the run's status.** The run succeeded in producing an
artifact; verification is its own fact, with its own timestamp and code. Conflating them would turn
a transient read failure into a "failed backup" that retention then treats as disposable.

R-G4. **`Verified` is advisory, not a restore guarantee.** What verification establishes: at the
moment of the read the artifact's bytes hash to what the sidecar recorded, the member list is within
the layout and bounds, the manifest parses and names this account and this backup, the sidecar and
manifest agree, and each dump is present (and matches its recorded digest if R-A5 chose to check it).
What it does not establish: that the SQL loads, that the files inside are the customer's intended
files, that the databases exist in the form to be restored into, that the host has room to restore,
that a remote copy exists, that the artifact will still be unchanged tomorrow, or anything against a
coordinated replacement (R-B2). The words the panel shows, the docs and the alert mail MUST NOT say
"restorable", "safe" or "guaranteed"; they say "verified" and give its date.

R-G5. State transitions are methods on the entity (`MarkVerified`, `MarkVerificationFailed`),
nothing public-settable, and a later verification overwrites an earlier one (the last finding wins).
A backup that is deleted or being restored is not verified.

### H. Alert mail

R-H1. The administrator alert for a verification failure carries the account's name, the backup's
identifier, the time, and a one-line localized KIND (from the closed code set). It carries NO path,
no file or member name, no digest, no size, no tool output, no agent message and no text derived
from the artifact (rules/security.md item 8; the existing `BackupAlertMailer` rule). Fields that
come from the customer (account name) are the same ones the other backup alerts already carry and
are placed in the body as text, never in the subject through a template that interprets them, and
carry no newline (rules/security.md item 4).

R-H2. It is sent through `OperatorAlertMailer` (the module does not hold the mail credential), is
journalled `AlertRaised`, and is raised for scheduled AND manual runs, because a manual run's failure
is still an unattended finding. It is raised once per failed verification of one backup per day or
per state change, not once per click, so that a customer cannot use "Verify now" as a mail cannon
against the administrator; the rule is a requirement, the exact window is the code's decision and
the reviewer checks it exists.

R-H3. An unverifiable-for-now outcome (R-G2) mails nothing.

### I. The panel endpoint

R-I1. Authorization: administrators and the owning account's logins, with the same policy the other
backup endpoints declare; `[ViewAsAccess]` declared; refused while viewing as if that is what the
neighbouring endpoints do. The endpoint declares its permission (rules/security.md item 6) and a
test proves a login with no backup permission is refused with the module's usual forbidden shape
and a login of another account gets 404 (R-E1).

R-I2. Audit: `BackupVerified` and `BackupVerificationFailed` are written through the module's
journal, with the backup id and actor, and no artifact-derived text (R-F2).

R-I3. The route takes only the backup's id. No body field selects an account, a path or a
destination: the destination is the one recorded on the backup row.

## What a reviewer must check

- `agent/crates/ops/src/backup/verify_backup.rs`: one `pub fn`, composing the existing pieces; no
  extraction of `home/`; no `std::fs` on a customer-derived path outside the descriptor-checked open;
  every spawn is `run_bounded` with a `CallClass` that is not overridden at the call site; a
  `Bound` is a refusal and cannot become an `ok`. Check the three ways out separately (a typed
  finding, a system failure, an ok) and that no path produces an `ok` with a read that did not
  complete.
- The manifest and dump members are refused when they are not regular files (R-A2), by a test that
  builds an archive whose `manifest.json` is a symlink and whose `databases/x.sql` is a hardlink, and
  watches the verification refuse. The layout scan alone does not catch these: confirm the test
  fails without the check.
- Containment (R-C): path derived from validated types and the descriptor open, `O_NOFOLLOW`,
  regular-file and link-count checks; a `backup_id` with a separator is refused before any open.
- Account against backup (R-E2): the manifest's account AND backup id are compared with the request;
  an artifact under another account's directory does not resolve; a mismatch test per direction.
- The lock (R-D2): taken first, never waited on, `AlreadyRunning` is distinct from a corrupt finding
  on the wire and in the panel (R-G2).
- If scratch is used: floor, root-only inode discipline, reaped on every path (R-D4).
- Errors (R-F): no path, member name, digest or tool output reaches the wire message or the
  `VerificationFailureCode`; any archive-derived name goes through `refused_member_name`.
- `proto/agent/v1/backup.proto`: additive only; every message, field and rpc commented with meaning
  and validation expectations, including "read-only, idempotent, local destination only, does not
  establish restorability". The revision moves in all four places together (`PROTO_VERSION`, the
  `REVISIONS` digest, `handshake.rs`, `AgentProtoVersionWindow`); confirm the digest placeholder, if
  any, is gone.
- `installer/systemd/maran-agent.service` is unchanged by this diff. If it is not, this note covers
  neither the edit nor its reason.
- `AgentCapability`: the Backups module already declares the backup capability; the new client method
  does not require a new capability value, and the capability guard test still passes.
- The panel: tenant-filtered lookup, IDOR 404 test, the agent receives the account and id from the
  row; verification failure never mutates the run status; the could-not-run outcomes never write
  `Failed` (R-G2); the alert carries the closed code and nothing else (R-H1); the rate rule of R-H2;
  the migration is expand-only (`maran migrate guard`).
- The docs: docs/backups/verification.md and docs/backups/alerts.md say R-G4 and R-B2 in plain words
  and state the remote-destination limit.

## What the author could not verify

The code does not exist when this note is written. Nothing below has been run.

- That the existing `tar --list` pre-scan reads names AND member types, or names only. If it reads
  names only, the manifest-and-dump regular-file requirement (R-A2) needs a second read (verbose
  listing, parsed under the same caps) that no existing unit provides. The author did not read the
  listing format code to the end.
- Whether the archive code can compute a dump member's digest without writing it to disk. The
  existing extraction writes dumps to the root-only scratch; a streaming `tar --to-stdout` of one
  member would need a bounded-output spawn variant that hashes rather than captures. If neither is
  affordable, R-A5 makes the limit a stated one; which was chosen is for the reviewer to read in the
  doc comment.
- How long a verification of a very large artifact takes on a slow disk against the call-class
  deadlines, and whether one deadline is wide enough for the 64 GiB creation ceiling. A deadline hit
  is a `Bound` and a non-finding (R-G2), which is the safe direction but could make verification
  permanently unavailable for the largest accounts. Raising a class is done once in `call_class.rs`
  with its reason.
- The cost of verifying after every run on a host with many accounts finishing at the same hour:
  how many verifications run at once, how long the agent's blocking pool and the backup lock are
  held, and whether R-D3's bound is enough. Not measured.
- Behaviour under the agent's real sandbox (cleared environment, `ProtectSystem`, syscall filter,
  `PrivateTmp`) for the added reads. The unit file is not touched; if the polygon shows a read
  needing a unit change, the work **stops** and the change belongs to the owner.
- Behaviour on every supported family and architecture (Ubuntu 22.04 and 24.04, Debian 12 and 13,
  AlmaLinux 9 and 10, Rocky Linux 9 and 10; `x86_64` and `aarch64`): the `tar` and gzip builds differ,
  and so may how they report a truncated gzip stream or a member of an unexpected type. The fixtures
  that prove a corrupt artifact is refused must be recorded, not written by hand.
- Whether a corrupt gzip stream that `tar --list` reads to a clean end on one build is a failure on
  another. The artifact digest (R-B1) is the control that does not depend on this, and is why it is
  required first.
- The mail-rate window (R-H2), and whether a flood of verification failures from one fleet of
  accounts reaches the administrator as a usable signal rather than noise.
- A coordinated replacement of artifact, sidecar and manifest (R-B2) is out of what verification can
  detect, and no control in this change addresses it.
