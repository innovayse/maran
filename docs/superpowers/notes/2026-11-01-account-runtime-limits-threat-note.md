# Account runtime limits (issue #133) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This
change adds an rpc through which the panel makes root run `systemctl set-property`. Written before
the code, per that rule; the branch may land on `dev` but must not merge to `main` until a second
reviewer has read this note and checked the items under "What a reviewer must check".

## The surface

A new rpc, `AccountsService.SetAccountResourceLimits(username, cpu_quota_percent, memory_max_mib,
tasks_max)`, served by the agent as root. It resolves the account against the password database
(`AccountOperations::resolve_managed`, the same proof every other account rpc uses) and runs, with
an argv array and no shell:

```
/usr/bin/systemctl set-property -- user-<uid>.slice CPUQuota=<n>% MemoryMax=<n>M TasksMax=<n>
```

with `CPUQuota=`, `MemoryMax=infinity`, `TasksMax=infinity` for a ceiling stated as unlimited.

Callers: `CreateAccountCommandHandler` (every new account, unlimited included),
`UpdatePlanCommandHandler` (every account on a plan whose ceilings changed) and
`ChangeAccountPlanCommandHandler` (one account moved to a plan with other ceilings). All three are
administrator endpoints; nothing a customer sends reaches the rpc.

## What an attacker could try, and why it does not work

1. **Cap or uncap somebody else's slice — root's, a system service's, another tenant's.** The
   unit name is built from the uid the password database gives for a *managed* account, never
   from a caller's text. `ManagedAccount::resolve` refuses reserved names, uids and gids below the
   host's `login.defs` floor, and a home outside `/home/<name>`; the refusal happens before any
   spawn (test `a_system_identity_is_refused_so_roots_slice_is_never_capped`, and the service
   census `every_account_rpc_refuses_root_with_invalid_input_and_runs_nothing`).
2. **Inject a second property or an option through a figure.** Every assignment is rendered from a
   validated `u32` (`CpuQuotaPercent`, `MemoryMaxMib`, `TasksMax` in `agent-core`) or a fixed
   literal, so after the `=` there are only digits; `--` ends systemctl's options before the unit
   (rules/security.md items 3 and 4). The panel's validator refuses out-of-range figures first
   (`PlanDefinitionValidator`), with the same bounds.
3. **Read "no ceiling" off a forgotten field.** An absent wire ceiling, a ceiling with neither
   case set, and a zero figure are all refused as invalid input (`validated_resource_limits`);
   unlimited is a oneof case of its own. On the panel side `RuntimeLimits` refuses a limited zero
   and the validator refuses an unlimited ceiling carrying a figure.
4. **Starve the host by setting absurd figures.** The figures only ever *lower* what one slice may
   use; the largest accepted values are no limit in practice. Nothing here can raise a limit above
   systemd's own defaults for other units.
5. **A uid recycled from a deleted account inherits its predecessor's ceilings.** `set-property`
   without `--runtime` persists drop-ins under `/etc/systemd/system.control/user-<uid>.slice.d/`,
   and account deletion does not remove them. Creation therefore ALWAYS writes all three
   properties (unlimited included) before the account row exists, so a new owner of the uid
   starts from its own plan's set, never a leftover one. If that write fails the system user is
   removed again and the creation is refused.

## What this does NOT protect, stated so nobody believes it does

- **php-fpm workers are outside the slice.** Each PHP version runs one php-fpm master as a system
  service, and every pool's workers are its children, in its service cgroup — not in the
  account's `user-<uid>.slice`. A busy PHP site is bounded by `pm.max_children`
  (`Plan.MaxPhpWorkersPerPool`) and nothing else. Moving pools into the slice needs a php-fpm
  master per account (a templated unit with `Slice=user-<uid>.slice`), which is a separate change.
- **Cron jobs on the Debian family are outside the slice.** Debian's `/etc/pam.d/cron` pulls in
  `common-session-noninteractive`, which carries no `pam_systemd`, so a cron job registers no
  session and runs in `cron.service`'s cgroup. The RHEL family's `crond` PAM stack includes
  `password-auth`, whose `-session optional pam_systemd.so` places jobs in the user's slice — read
  from the distributions' shipped PAM files, NOT verified on a host here.
- **What IS inside:** every session `pam_systemd` registers for the user — SSH and SFTP logins —
  and anything started under them.
- The polygon proof the issue asks for (a busy loop throttled to its cap while another account
  stays responsive) needs a booted systemd with cgroup v2 delegation and was **not run**; the
  container polygons use a `systemctl` stand-in that accepts `set-property` and does nothing.

## What a reviewer must check

- That `ManagedAccount::resolve` is reached before the spawn on every path
  (`ops/src/accounts/set_account_resource_limits.rs`).
- That no caller text reaches the argv (`model/account_resource_limits.rs`, the three validated
  types in `agent-core/src/validation/system/`).
- That `systemctl set-property -- user-<uid>.slice …` is accepted with the `--` on both families'
  shipped systemd (measured on neither here).
- The rollback in `CreateAccountCommandHandler` when the ceilings are refused.

## What the author could not verify

- Behaviour on a real host of either family: the effect of the properties on a running slice, the
  persistence of the drop-ins across a reboot, and the `--` handling above.
- The RHEL cron placement described above.
