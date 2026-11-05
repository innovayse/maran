# SFTP public-key login (issues #135, #339)

A customer can add SSH public keys to each of their SFTP logins and log in with the key instead of, or
beside, the password. Keys are managed from the SFTP logins page: the "Keys" action on a login opens the
list, the add form and the remove action.

- **Add.** Give the key a label (up to 64 characters, no control characters) and paste the public key as
  one line. The panel computes the fingerprint (`SHA256:` plus unpadded base64) itself and shows label,
  algorithm, fingerprint and the time it was added. The key body is never shown again.
- **Allowed algorithms** (the same grammar as the server agent, `ssh_public_key.rs`): `ssh-ed25519`,
  `sk-ssh-ed25519@openssh.com`, `ecdsa-sha2-nistp256`, `ecdsa-sha2-nistp384`, `ecdsa-sha2-nistp521`,
  `sk-ecdsa-sha2-nistp256@openssh.com`, and `ssh-rsa` with a modulus of 2048 to 16384 bits. At most
  8 KiB per key. No leading option (`command=`, `from=` and the like are refused), no control
  character, the trailing comment is dropped. DSA is never accepted.
- **Limit.** At most 32 keys per login. The 33rd is refused with a conflict. The same key twice on one
  login is a conflict; the same key on two logins is allowed.
- **Remove.** The panel replaces the login's key file with the remaining set and then ends every open
  session of that login (#339). That includes sessions opened with the password or another key, and an
  upload in flight is cut at the byte it had reached (the partial file stays). The confirm dialog says
  so. If ending the sessions fails, the key stays listed and an error says the key is already refused
  for new logins but sessions may remain; repeat the removal to end them.
- **Locked logins.** Keys are stored either way. For a login that cannot sign in (a suspended account)
  the agent parks the key file under a name sshd does not read, so the key does not work until the
  account is active again. The panel adds no lock logic of its own.
- **View as.** An administrator viewing the panel as a customer may list keys but cannot add or remove
  them (refused): a key is a credential that outlives the look.
- **Audit.** `SftpKeyAdded` and `SftpKeyRemoved` record the actor, login name, label, algorithm and
  fingerprint. Never the key body, the pasted comment, or the agent's raw text; a failure carries the
  error code.
- **Verify.** From a client holding the private key: `sftp -i ~/.ssh/<key> <login>@<host>`. After a
  removal, the same command must be refused and any session opened earlier must have closed.
- **Limits of what is observed.** The panel keeps the key set; the agent writes the file. No periodic
  reconcile compares them: if the file is edited by hand as root, or a compensating write after a failed
  commit also fails, a key can stay live but unlisted until the next add or remove on that login (which
  resends the full set) or the login's deletion. A connection that is mid-authentication at the instant
  of a removal could finish with the old key. Neither is observed by the agent.

Threat note: `docs/superpowers/notes/2026-11-05-sftp-public-keys-threat-note.md`. Second review of this
change is OUTSTANDING.
