# Threat note: customer-managed backup schedule (issue #147)

`rules/security.md` asks for a threat note when a change widens who can cause root work on the host.
This change adds no agent code and no rpc, but it lets a customer start unattended backups of their
own account, which `BackupSchedulesController` had reserved to administrators. It is written BEFORE
the code, and every statement is a requirement the code must meet; where the code lands and differs,
the code is wrong or this note is amended in the same change.

**Second reviewer: OUTSTANDING (#331).** The work is done by an agent session, which cannot obtain
one. This is a smaller surface than a change to the agent (no new privileged operation, the runner,
retention, verification and destinations are unchanged), and whether it needs a human reviewer before
it reaches `main` is the owner's decision, not the author's. Until that decision is recorded the
branch MUST NOT merge to `main`.

## What is added

`GET` and `PUT /api/v1/me/backup-schedule`: the signed-in customer's own schedule row, the same
`backups."BackupSchedules"` row an administrator's per-account override uses (one row, one meaning).
`GET /api/v1/backup-options` gains `customerFrequencies` and `customerRetainCounts`, so the form's
lists are produced by the server. The write moves into one `BackupScheduleWriter` that the
administrator's handler now shares; its behaviour is unchanged.

No new daemon, port, outbound call, dependency, migration or agent rpc.

## What an attacker could do, and why it is safe

1. **Aim a schedule at another account.** The account is never read from the request. The command's
   account member carries `[property: JsonIgnore][property: BindNever][BindNever]` and is written by
   the action from `CurrentUser.Caller` (a `CustomerCaller`); `BackupsCommandBindingGuardTests` holds
   the attributes and a body test shows a JSON body naming another account's id is discarded. The
   handler then resolves the account through the tenant-scoped `IAccountDirectory` and writes the row
   of the account the directory RETURNED, so a stamp that were ever wrong would answer 404, not write.
   The integration test sends a body naming another account and finds only the caller's own row.
2. **Make a schedule that belongs to nobody (the host-wide policy).** `AccountId` on the customer
   command is a non-nullable `Guid`, and the writer is given the directory's account. A caller with no
   account of their own (an administrator, a reseller, an account-less login) is stamped with an
   empty id, which the directory answers as an account that does not exist: 404, never 403, and no
   row. This matters because the module's query filter shows an account-less caller the host-wide row
   (`AccountId == null` equals a null tenant), so the customer handlers deliberately do not rely on the
   filter alone for the account.
3. **Aim a backup at a destination of their choosing.** The command has no destination member. An
   existing row keeps the destination an administrator gave it; a new row gets the default (`null`).
   `CreateBackupCommand` refuses to carry a destination for the same reason.
4. **Fill the operator's disk or the host with load.** A customer chooses daily or weekly only
   (an explicit allow-list in `CustomerBackupLimits`, not "every member of the enum"), an hour, and
   keeps 1 to 7 copies (the ceiling is a separate, smaller number than the entity's 365; owner
   decision D3 may change it in that one constant). The existing agent free-space floor (#148) and the
   per-account backup lock still apply, and the unique index holds one schedule per account, so two
   writers cannot double the nightly load by that route.
5. **Override the operator's policy.** The customer writes the SAME per-account row an administrator's
   override uses. An operator who needs to stop customer scheduling does so with the plan feature
   (`AccountFeature.Backups`, which the controller requires on every request) or by saving the row
   disabled, and the audit entry (`BackupScheduleSaved`, actor and address) names who last wrote it.
   A customer cannot touch the host-wide policy: see item 2.
6. **View-as.** An operator viewing as a customer may read the schedule (`ViewAsAccess.Read`) but not
   write it (`ViewAsAccess.Refused`): a schedule is running work that outlives the look.
7. **Bypass the administrator-only surface.** `BackupSchedulesController` is untouched and stays
   `AdminOnly`; the integration test shows a customer is still answered 403 there.

## Known limits (not hidden)

- **A host-wide policy and a customer's own schedule both fire for that account.** The sweep treats
  every enabled row as its own schedule: the host-wide row covers all accounts and a per-account row
  covers one, so an account with both is backed up twice a night. This is how the administrator's
  per-account override already behaves (the entity says "two schedules for one account would take two
  backups a night"); it is not introduced here, but a customer can now cause it without an operator's
  action. Whether a per-account row should replace the host-wide one for its account is a design
  question for the owner, not changed by this note.
- **The number 7 appears in the error text** `BackupScheduleCustomerRetainTooHigh` as well as in
  `CustomerBackupLimits.MaximumRetainCount`; a change of the ceiling edits both.
- The retention a customer chooses applies to the account's scheduled backups only, as for any
  schedule; it prunes nothing the customer's manual backups hold.

## What a reviewer must check

1. The command's `[JsonIgnore]`/`[BindNever]` pair on `AccountId`, and that the handler writes the
   directory's account, not the command's field.
2. The 404 for a caller with no account, and that no customer path can produce a row with
   `AccountId == null`.
3. That `SaveBackupScheduleCommandHandler` behaves as before now that it calls the shared writer (the
   existing `BackupScheduleTests` and handler tests are the evidence).
4. The thing the author could not verify: the double-backup interaction above on a real host with
   both rows enabled (reasoned from `BackupRunHandler`, not exercised). The free-space floor of #148
   was confirmed present (`agent/crates/ops/src/backup/backup_host.rs` and `backup_error.rs` name it);
   its behaviour was not exercised either.
