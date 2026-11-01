# The new account form

Accounts → New account (`/accounts/new`, administrators only) creates a hosting account. This page
describes each field, what the server enforces, and what creation makes on the host. The SPA checks
the same rules before sending, but only as advice: the server validates every field again and its
answer is what counts.

## Fields

| Field | What it is | What the server enforces |
|---|---|---|
| Primary domain | The account's main domain, entered without `www.` | A valid host name of at most 253 characters, not taken by another account, and not starting with `www.` (`AccountDomainWwwPrefix`) |
| Username | The account's system user name. Filled in from the first label of the domain as you type (`www.my-shop.example.com` suggests `myshop`) until you type in it yourself | 3 to 30 characters: a lowercase letter, then lowercase letters, digits or underscores (`AccountNameInvalid`); not a reserved system name (`AccountNameReserved`); not taken (`AccountNameTaken`). The server agent checks the name again |
| Plan | The plan whose disk quota, counts and CPU, memory and process ceilings bound the account | Must exist (`PlanNotFound`) |
| Owner email | Where the owner's invitation is sent | A bare e-mail address of at most 256 characters |
| Owner language | English, Russian or Armenian; starts as your own interface language | One of `en`, `ru`, `hy` exactly (`AccountOwnerLanguageUnsupported`). Optional in the API, English when absent |

The owner language is stored on the account (`accounts.Accounts.OwnerLanguage`). The invitation is
written in it when the account is created and every time it is resent, whatever language the
administrator who sends it is using. Accounts created before this field existed read `en`.

## What creation makes on the server

1. A system user named after the username, owning everything the account holds.
2. Its home directory, with the plan's disk quota.
3. The plan's CPU, memory and process ceilings on the user's systemd slice (#133). If they cannot
   be applied, the system user is removed again and nothing is recorded.
4. The account row, an audit entry, and the owner's panel login (Customer role) with an invitation
   mailed to the owner email. The owner sets their own password; the administrator never knows it.

## Not yet on the form

Issue #194 also asks for: an initial password with a generator instead of the invitation, the IP
address, the reseller flag, per-account inode, process and open-file limits, shell access, the
backup and automatic certificate options, and the chosen options in the audit entry. They are not
in this release.
