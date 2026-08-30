## What this changes

<!-- Why, not what the diff already shows. If it fixes an issue, write "Closes #N". -->

## How it was verified

<!-- The commands you ran and what they said. "Tests pass" is not verification; a paste is. -->

- [ ] `maran structure`, `maran format --check`, `maran migrate check`
- [ ] `dotnet test` (backend), `maran agent check` (agent)
- [ ] `npm run lint && npm run typecheck && npm run build`, Playwright (frontend)
- [ ] New or changed gates were proved able to fail

## Threat note

<!--
REQUIRED for changes to authentication, sessions or tokens, the agent's privs module,
licence verification, or the installer's privileged steps (rules/security.md). Those also
need a second reviewer.

Answer: what could an attacker do with this surface, and why is it safe now. Name what you
left open as well as what you closed — a note that lists only the safe parts is not one.

Delete this section if the rule does not apply.
-->

## Anything a reviewer should look at first

<!-- A decision you are unsure about, a trade-off you made, a defect you found on the way. -->
