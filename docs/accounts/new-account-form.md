# The new account form

Accounts → New account (`/accounts/new`, administrators and resellers) creates a hosting account. This page
describes each field, what the server enforces, and what creation makes on the host. The SPA checks
the same rules before sending, but only as advice: the server validates every field again and its
answer is what counts.

## Fields

| Field | What it is | What the server enforces |
|---|---|---|
| Primary domain | The account's main domain, entered without `www.` | A valid host name of at most 253 characters, not taken by another account, and not starting with `www.` (`AccountDomainWwwPrefix`) |
| Username | The account's system user name. Filled in from the first label of the domain as you type (`www.my-shop.example.com` suggests `myshop`) until you type in it yourself | 3 to 30 characters: a lowercase letter, then lowercase letters, digits or underscores (`AccountNameInvalid`); not a reserved system name (`AccountNameReserved`); not taken (`AccountNameTaken`). The server agent checks the name again |
| Plan | The plan whose disk quota, counts and CPU, memory and process ceilings bound the account | Must exist (`PlanNotFound`). A reseller sees only its own plans, and the plan must belong to the same owner as the account (`PlanOwnerMismatch`) |
| Owner (reseller) | Administrators only: which reseller owns the account, or none for the administrator's own. A reseller creating an account always owns it and is not shown the field | Must exist (`ResellerNotFound`); a reseller naming any other reseller gets the same 404 |
| Owner email | Where the owner's invitation is sent | A bare e-mail address of at most 256 characters |
| Owner sign-in | `Mail an invitation` (the default) or `Set an initial password`. The second is for a host that cannot send mail: type a password or press the generator beside the field | An initial password is required in that mode and refused in the other (`loginMode`, `initialPassword`). Its strength is judged by the same policy as every other password, and that is decided BEFORE anything is created: a weak password is refused with the code of the rule it breaks (`AccountInitialPasswordTooShort`, `AccountInitialPasswordTooLong` or `AccountInitialPasswordSameAsName`; `AccountInitialPasswordInvalid` is only the pairing of the mode and the password) and leaves no system user, no account row and no login behind. The owner is made to replace it at the first sign-in, and the password is never logged, audited or shown again |
| Process, inode and open-files limits | Per-account limits. Empty follows the plan (the process field shows the plan's ceiling as its placeholder); a figure applies to this account only | A process limit is at least 1; inodes and open files accept `0` for no limit. A reseller may only lower a limit below its plan, an administrator may set any value in range. Optional in the API: absent means the plan decides |
| Shell access | `shellAccess`, API only for now. Whether the account's system user gets a bash shell for SSH and SFTP logins instead of none. Off by default: a shell is a way to run code as the account | A boolean. The shell is kept across a suspension: unsuspending gives the account back the shell it was created with |
| Backups | `backupEnabled`, API only for now. Whether the account is included in scheduled backups. On by default | A boolean. Turning it off is recorded, and the Backups module reads it when it hears of the new account |
| Owner language | English, Russian or Armenian; starts as your own interface language | One of `en`, `ru`, `hy` exactly (`AccountOwnerLanguageUnsupported`). Optional in the API, English when absent |

The owner language is stored on the account (`accounts.Accounts.OwnerLanguage`). The invitation is
written in it when the account is created and every time it is resent, whatever language the
administrator who sends it is using. Accounts created before this field existed read `en`.

## What creation makes on the server

1. A system user named after the username, owning everything the account holds.
2. Its home directory, with the plan's disk quota.
3. The plan's CPU, memory and process ceilings on the user's systemd slice (#133). If they cannot
   be applied, the system user is removed again and nothing is recorded.
4. The account row, an audit entry listing every option the creator chose that is not the default (the
   initial-password mode, shell access, no backups, and each limit the account states for itself; never
   the password), and the owner's panel login (Customer role). By default an
   invitation is mailed to the owner email, so the owner sets their own password and the administrator
   never knows it. With an initial password no mail is sent: the login exists with the password the
   administrator chose, and the owner must replace it at the first sign-in. The administrator knows a
   credential until then, which is why the invitation stays the default.

Two options are the administrator's alone (issue #488): the login shell and the owner's first password.
A reseller, or an API key a reseller owns, is refused them with `AccountShellAccessAdministratorOnly` and
`AccountInitialPasswordAdministratorOnly` before anything is provisioned, so a reseller's account has no login
shell and its owner chooses the password through the invitation; the form offers a reseller the invitation only.

A reseller creating an account is also held to its envelope: the account count and the disk pool
(`ResellerAccountLimitReached`, `ResellerDiskPoolExceeded`). See [Resellers](resellers.md).

## Not yet on the form

Issue #194 also asks for the IP address and the automatic certificate option. They are not in this
release. Shell access and the backup choice are accepted by the API and carried out by the server, but
the form does not show them yet.
