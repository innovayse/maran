# Clock synchronisation

How the panel knows whether the server's clock is right, where it says so, and what it cannot
see (issue #150).

## Why it matters

Two-factor codes, sign-in sessions, certificate validity and the licence lease all compare against
this server's clock. When the clock drifts, each of them fails in its own confusing way: a correct
authenticator code is refused, a fresh session expires at once, a valid certificate reads as not yet
valid.

## What is read

The server agent runs `timedatectl show --property=NTP --property=NTPSynchronized` (rpc
`MonitorService.GetClockSync`, contract revision 10) and reports one of three states:

| State | `NTPSynchronized` | `NTP` | Meaning |
|---|---|---|---|
| `synchronized` | yes | any | The kernel reports its clock synchronised. |
| `notSynchronized` | no | yes | A synchronisation service (systemd-timesyncd on the Debian family, chrony on the RHEL family) is enabled but has not synchronised the clock. |
| `syncServiceDisabled` | no | no | No synchronisation service is enabled; nothing corrects a drift. |

`NTPSynchronized` is the kernel's own flag. The kernel keeps an estimate of how far its clock may
have strayed and stops reporting it synchronised once that estimate passes sixteen seconds, which
happens when nothing has disciplined the clock for some hours. That is how a drifting clock shows up
here.

A reading the agent could not take — timedated unreachable, the program killed at its deadline, an
answer without both properties — is an error, never `notSynchronized`.

## Where the panel says so

- **Dashboard.** "Needs attention" carries "Server clock not synchronised" when the state is
  anything but `synchronized`, linking to the monitoring screen; "The clock could not be checked"
  when the agent could not be asked.
- **Monitoring screen.** A "Clock" card with the state and the backend's explanation, drawn as a
  warning when the clock is not synchronised. Read live from `GET /api/v1/monitoring/clock`
  (administrators only): `{"state": "notSynchronized", "description": "…"}`.
- **Alert mail.** The sampler reads the clock every round. After ten consecutive rounds that are not
  synchronised the operator is mailed once ("The server's clock is not synchronised"), and once
  more when it is synchronised again. The mail takes the same path as every other operator alert
  (`OperatorAlertMailer`), and the journal records `Alert raised` / `Alert resolved` with the subject
  `ClockUnsynchronized:system-clock`.
- **A refused two-factor code.** When the latest reading found the clock not synchronised, a
  refused code at sign-in, at enrolment or when switching two-factor off answers
  `InvalidTwoFactorCodeClockUnsynchronizedUnauthorized` (401) instead of
  `InvalidTwoFactorCodeUnauthorized`, with a sentence saying the server's clock may be why. The code
  is still refused. The hint is read from the latest monitoring round, not from the agent, so it
  can lag by one sampling interval.

## What it cannot see

- An offset. A clock a few seconds wrong that the kernel still reports synchronised is not caught;
  measuring an offset needs a time server, and the agent makes no outbound calls.
- A synchronisation daemon timedated does not know of. Its flag still decides: if the kernel says
  synchronised, the state is `synchronized` whatever `NTP` reads.

## Fixing it

Enable a synchronisation service and let it reach a time server:

```
timedatectl set-ntp true          # Debian family: systemd-timesyncd
systemctl enable --now chronyd    # RHEL family: chrony
timedatectl                       # "System clock synchronized: yes"
```

The panel never changes the time or the synchronisation setup itself.
