# Threat note: the agent's free-space floor (issue #148)

`rules/security.md` ("Sensitive change escalation") asks for a threat note when a change touches the
agent's privileged surface. This change adds a refusal to four root operations and a flag to the
root daemon's command line. The note is written before the code.

**The second reviewer requirement is OUTSTANDING.** The work is carried out by an agent session,
which cannot obtain one. Branch `fix/148-free-space-floor` MUST NOT merge to `main` until a human
second reviewer has read the change against this note. The owner is told so at hand-off.

## What changes

- `maran-agent` takes `--free-space-floor-mib <n>`: the number of mebibytes an unprivileged writer
  must still be able to add to a filesystem before the agent starts work that writes into it. The
  installer writes it into `/etc/maran/agent.env` (`MARAN_AGENT_FREE_SPACE_FLOOR_MIB`, 1024 by
  default, an operator's value kept across re-runs) and the unit passes it. When the flag is absent
  the agent applies the same 1024 MiB; the usage text says so. Zero, a non-number and a value whose
  byte count overflows are refused at start-up: a floor that cannot be read is not a floor of zero.
- Before it writes, each of four operations measures `f_bavail` of the filesystem it is about to
  write into and refuses with the new `ERROR_CODE_BELOW_FREE_SPACE_FLOOR` when less than the floor
  is left: `CreateAccount` (the account home root), `CreateBackup` (the account's backup directory,
  where the artifact lands; the bulk scratch keeps its existing per-dump ceiling), `RestoreBackup`,
  the extraction (the account home root), and `WriteFile`, the only upload path (the account home
  root). The archive extraction rpc `ExtractArchive` is declared in the contract and answers
  `UNIMPLEMENTED`, so it has no write to guard. A filesystem that
  cannot be measured is refused as a system failure, never admitted.
- `HostMetrics` gains `free_space_floor_bytes`, so the panel can raise its "approaching the floor"
  alert before the agent starts refusing. Contract revision 7.
- A backup whose sidecar cannot be written in full after the artifact was published (a disk that
  filled between the rename and the sidecar's `fsync`) now removes the sidecar it created and the
  artifact it had just published, instead of leaving a published artifact with no description.

## What an attacker could do with it, and why it is safe

1. **Turn the floor off or lower it.** The value is read only from the agent's own command line,
   which only root (the unit file and `/etc/maran/agent.env`, `root:root 0644`) can change. Nothing
   in a request carries a floor: no rpc field, no header, no panel setting. A compromised panel
   process (`maran`) therefore cannot disable it; it can at most read the value back through
   `GetHostMetrics`, which discloses a number the operator chose and nothing about any account.
2. **Use the refusal to deny service.** The refusal fires only when the filesystem is already
   within the floor of full. Filling it to that point needs write access to it, which a hosting
   account has inside its own quota and nowhere else; a host whose quotas are not enforceable is
   already alerted on (`QuotaNotEnforceable`). Once below the floor, refusing new accounts, backups,
   restores and uploads is the intended behaviour: each of them would otherwise fail part-way on
   `ENOSPC`, which is the worse outcome the issue describes. Suspension, deletion, quota changes,
   certificate removal and every read keep working, so an operator can free space with the panel.
3. **Learn something through the error.** The wire error carries the available bytes and the floor
   in its operator-facing `message`; the panel logs that text and sends the customer only the
   localized sentence for `AgentBelowFreeSpaceFloor`, which names no path, size or tool output
   (rules/security.md item 8). The figures concern the host's filesystems, not another account.
4. **Race the measurement.** The check is a pre-flight `statvfs`, not a reservation: another writer
   can fill the disk after it passes. That is not a bypass, it is the limit of a floor, and the
   write paths behind it keep their own all-or-nothing discipline — `WriteFile` writes a temporary
   name and renames, a backup writes `.partial` and renames and removes both on any failure, a
   restore keeps the previous home until its swap. The floor reduces how often those paths meet
   `ENOSPC`; it is not what makes them safe when they do.
5. **Make a backup leave a half-written artefact.** The new sidecar clean-up removes only names
   this call created: the sidecar is opened with `create_new` (a pre-placed file or symlink is
   refused before anything is written and left alone, unchanged from before), and the artifact
   being removed is the one this call published under a backup id no other backup is minted with.
   No path in the clean-up comes from a request.
6. **Smuggle a value through the flag.** The flag is parsed into a `FreeSpaceFloor` that holds a
   byte count and nothing else; it reaches no template, path or argv.

## What a reviewer must check

- That every `statvfs` is asked of the directory the write lands in, and that the four operations
  measure before their first write (account creation after the existence check and before
  `useradd`; backup after the already-published check and before the scratch exists; restore before
  anything is staged or extracted; upload before the parent directories are created).
- That an unmeasurable filesystem is refused, in each of the four areas' error mappings.
- That the sidecar clean-up cannot remove anything the operation did not itself create.
- That `--free-space-floor-mib` is refused when malformed, and that the installer's generated
  `agent.env` always carries a number.

## What the author could not verify

- **A real `ENOSPC` on a real filesystem.** The unit tests drive the decision through injected
  measurements and simulate the archiver running out of space by a fake that writes part of the
  artifact and then fails as `tar` does; no test fills a real filesystem. The sidecar clean-up in
  particular is reached only by a write failing after the archiver succeeded, which no unit test
  can provoke on a filesystem the test does not own.
- **`/home` on its own mount.** The panel's alert compares the floor with the ROOT filesystem's
  available space, because that is the filesystem `HostMetrics` measures. When the account homes
  live on another filesystem, the agent's refusal still follows that filesystem, but no alert
  precedes it.
