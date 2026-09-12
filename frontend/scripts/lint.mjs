// The SPA's lint gate: four sub-checks, all of which run, all of which report.
//
// This replaces `oxlint . && eslint . && node scripts/check-locale-keys.mjs && node
// scripts/check-page-routes.mjs`. Under `&&`, the first sub-check to fail silenced every later
// one, and the engineer reading a red lint had no way to know a whole gate had been skipped. That
// is not a hypothetical either: a locale-key failure on this branch hid `check-page-routes`
// entirely — the newest and largest SPA structural check, the one that catches a page nothing
// links to — so the branch was measured against three gates while reporting four.
//
// It is the same failure `suite_compare` exists to catch on the test side (rules/testing.md): a run
// that stopped early looks exactly like a run that finished, and the number that would have said
// otherwise is the one that never got printed. So this runner follows the house shape the backend
// and agent lanes use — every sub-check runs to completion, its verdict is collected, and the
// command's answer is a PRINTED verdict line plus a collected total, never an exit code alone.
//
// Sequential, not parallel, and deliberately so: four processes writing to one terminal interleave
// into output nobody can attribute, and eslint — which is type-aware here and owns almost all of
// the roughly two minutes a full run takes — already saturates the machine by itself, so running
// the others alongside it buys close to nothing. The order is kept from the previous chain:
// oxlint's fast pre-pass first, ESLint the authority after it (rules/vue.md), then the two script
// checks, which cost under a second between them.
//
// The trade this makes is explicit. Under `&&`, an oxlint failure saved the two minutes eslint
// would have cost; now every check runs every time. That is the right way round: a gate exists to
// tell you everything that is wrong in one pass, and an engineer who fixes one finding only to
// meet the next one two minutes later has been billed the same time twice anyway — with a wrong
// picture of the branch in between. What changed is only that a failure no longer stops the ones
// behind it.

import { spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import process from 'node:process'

/** Repository-relative root of the SPA, resolved from this file's own location. */
const FRONTEND_ROOT = new URL('..', import.meta.url).pathname

/** Where a locally installed linter's executable lives. */
const LOCAL_BIN = join(FRONTEND_ROOT, 'node_modules', '.bin')

/**
 * The sub-checks, in the order they run.
 *
 * `verdict` is the pattern the sub-check's own summary line must match. A check that declares one
 * and does not print it FAILED, whatever its status: a script that dies half way through — an
 * exception in a loop, a directory that moved — can still leave a zero status behind, and its
 * summary line is the only evidence it reached the end. A check with no `verdict` is scored on its
 * status alone, which is stated in this command's own UNOBSERVED line rather than left implied.
 */
const CHECKS = [
  { name: 'oxlint', command: join(LOCAL_BIN, 'oxlint'), args: ['.'], verdict: null },
  { name: 'eslint', command: join(LOCAL_BIN, 'eslint'), args: ['.'], verdict: null },
  {
    name: 'check-locale-keys',
    command: process.execPath,
    args: [join(FRONTEND_ROOT, 'scripts', 'check-locale-keys.mjs')],
    verdict: /^check-locale-keys: /m,
  },
  {
    name: 'check-page-routes',
    command: process.execPath,
    args: [join(FRONTEND_ROOT, 'scripts', 'check-page-routes.mjs')],
    verdict: /^check-page-routes: /m,
  },
]

/**
 * Runs one sub-check to completion and decides its verdict.
 *
 * A check that declares a `verdict` pattern has its output captured so the line can be looked for,
 * and the output is written through afterwards; one that does not inherits the terminal, so a long
 * linter's progress is still visible while it runs.
 * @param {{name: string, command: string, args: string[], verdict: RegExp | null}} check The check.
 * @returns {{name: string, ok: boolean, reason: string}} Its name, whether it passed, and — when it
 *   did not — the reason in words rather than a status number.
 */
const runCheck = (check) => {
  process.stdout.write(`\n=== lint: ${check.name} ===\n`)

  if (check.command !== process.execPath && !existsSync(check.command)) {
    // A gate you cannot run is not a gate that passed (rules/testing.md): a missing binary is a
    // failure to verify, never an absence of findings.
    return { name: check.name, ok: false, reason: `not installed at ${check.command}` }
  }

  const captured = check.verdict !== null
  const result = spawnSync(check.command, check.args, {
    cwd: FRONTEND_ROOT,
    stdio: captured ? ['ignore', 'pipe', 'pipe'] : 'inherit',
    encoding: 'utf8',
    shell: false,
  })

  if (captured) {
    process.stdout.write(result.stdout ?? '')
    process.stderr.write(result.stderr ?? '')
  }

  if (result.error !== undefined) {
    return { name: check.name, ok: false, reason: `could not be started: ${result.error.message}` }
  }
  if (result.signal !== null && result.signal !== undefined) {
    // A killed run measured a fraction of itself; treating it as a finding-free pass is the
    // false pass this whole shape exists to prevent.
    return { name: check.name, ok: false, reason: `was killed by ${result.signal}` }
  }
  if (captured && !check.verdict.test(result.stdout ?? '')) {
    return { name: check.name, ok: false, reason: 'printed no verdict line — it measured nothing' }
  }
  if (result.status !== 0) {
    return { name: check.name, ok: false, reason: 'reported problems' }
  }
  return { name: check.name, ok: true, reason: '' }
}

/**
 * Runs every sub-check, prints the collected verdict, and fails when any of them failed.
 * @returns {number} The process exit code: 0 only when every sub-check ran and passed.
 */
const main = () => {
  const results = []
  for (const check of CHECKS) {
    const result = runCheck(check)
    results.push(result)
    process.stdout.write(`lint: ${result.name} ${result.ok ? 'OK' : `FAILED — ${result.reason}`}\n`)
  }

  const failed = results.filter((result) => !result.ok)

  // Vacuity guard on the axis that can go blind: this command's verdict is a count of checks, and a
  // count is satisfied just as well by having run none. A declared check that produced no result —
  // a loop that threw, a list that emptied — must not be able to read as agreement.
  const incomplete = results.length !== CHECKS.length || CHECKS.length === 0
  if (incomplete) {
    process.stdout.write(
      `lint: ${results.length} of ${CHECKS.length} declared checks produced a result — ` +
        'the run measured nothing\n',
    )
  }

  const ok = failed.length === 0 && !incomplete
  process.stdout.write(
    `\nLINT VERDICT: ${ok ? 'OK' : 'FAIL'} — ${CHECKS.length} checks declared, ` +
      `${results.length} run, ${results.length - failed.length} passed, ${failed.length} failed\n`,
  )
  for (const failure of failed) {
    process.stdout.write(`  FAILED: ${failure.name} — ${failure.reason}\n`)
  }
  process.stdout.write(
    'UNOBSERVED HERE — oxlint and eslint print no summary line of their own, so those two are ' +
      'scored on their exit status alone and a crash that exits 0 would read as clean; the two ' +
      'script checks are scored on their printed verdict line as well as their status. This ' +
      'command reports which checks ran, never what any of them cannot see — each prints its own ' +
      'blind spots above.\n',
  )
  return ok ? 0 : 1
}

process.exitCode = main()
