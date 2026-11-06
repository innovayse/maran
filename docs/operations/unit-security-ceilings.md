# Unit security ceilings

Every service unit the installer ships (`installer/systemd/*.service`) has one line in
`installer/systemd/unit-security-ceilings.txt`: the unit's name, a tab, and the highest
`systemd-analyze security` exposure score it may have. The first line records the systemd version
the ceilings were measured with. CI (`agent.yml`, job `units`) runs `maran units` and fails when a
unit scores above its ceiling, has no ceiling, or when the systemd on the runner is another major
than the recorded one. A template unit is keyed by its template name, `maran-php-install@.service`.

A lower score is better. The ceiling is a ratchet: it records how exposed the unit was when a
reviewer last accepted it, so hardening can only be lost on purpose.

## What it can and cannot tell you

- It scores the unit as the installer renders it, offline. It reads text; it never starts the
  service, so a directive that systemd accepts but the host does not honour is invisible to it.
- The score depends on the systemd release. A different major can score the same unit differently,
  which is why the recorded version is asserted and a mismatch is `DID NOT RUN`, not a failure.

## Raising a ceiling

A unit that legitimately needs a looser directive (a new path it must write, a capability it now
needs) will score higher and fail the job. Raising the ceiling is a reviewed change, never an
automatic one:

1. Make the unit change and say, in the pull request, which directive was loosened and why the
   service cannot work without it.
2. After `source scripts/dev`, run `maran units --accept`. It rewrites the ceilings file from a real
   scoring run. CI never runs `--accept`: a gate that refreshes its own ceiling enforces nothing.
3. Commit the ceilings file in the same pull request and let the reviewer read its diff beside the
   unit's: the diff of one number is the review artefact.

When a score drops below its ceiling, `maran units` prints a note rather than failing. Lowering the
ceiling the same way (`maran units --accept`) locks the improvement in.
