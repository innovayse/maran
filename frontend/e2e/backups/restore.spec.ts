import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackupRestore, stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** The only state a restore may be offered from: a run that produced an artifact. */
const COMPLETED: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  status: 'completed',
  kind: 'manual',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: '',
  failureDisplayName: '',
}

/**
 * Puts the backups screen in front of a signed-in caller with one completed copy of alice's
 * account, and opens the restore dialog on it.
 * @param page The Playwright page under test.
 * @returns Resolves once the dialog is on screen.
 */
const openDialog = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])
  await page.goto('/backups')
  // The row's commands live behind a per-row menu whose trigger names the row's account.
  await page.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await page.getByRole('menuitem', { name: 'Restore', exact: true }).click()
}

// The house standard for a destructive action is that nothing leaves the browser before the answer
// (e2e/firewall/whitelist.spec.ts). Restore is worse than removing a firewall exemption: it drops
// every database in the copy, and the first drop is the point of no return. So the answer is not a
// second click — it is the account's own name, typed — and the recorded bodies are what prove that
// a click, and a near-miss, both send nothing.
test('a restore sends nothing until the account name is typed exactly', async ({ page }) => {
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    whole: true,
    filesRestored: true,
    databasesRestored: 2,
    databasesTotal: 2,
    failureCode: '',
    failureDisplayName: '',
  })

  // Positive control: the dialog IS open and its submit control IS on the page, so the silence
  // below is a fact about what the page sends rather than about the probe.
  const submit = page.getByRole('button', { name: 'Restore this account' })
  await expect(submit).toBeVisible()

  await submit.click({ force: true })
  expect(submitted()).toEqual([])

  await page.getByRole('textbox', { name: 'Account name' }).fill('Alice')
  await submit.click({ force: true })
  expect(submitted()).toEqual([])

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await submit.click()

  await expect.poll(submitted).toHaveLength(1)
  // The body carries the confirmation and nothing else: the backup id travels in the path because
  // the server refuses to bind it from the body, and a body-bound id would let a caller confirm one
  // account's name and have another account's backup restored.
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice' })
})

// The panel already knows the name. Filling it in would leave the operation one click away again,
// which is the whole of what this dialog exists to prevent.
test('the confirmation field is never prefilled', async ({ page }) => {
  await openDialog(page)

  const field = page.getByRole('textbox', { name: 'Account name' })
  await expect(field).toBeVisible()
  await expect(field).toHaveValue('')
})

// A restore is replace-within-scope, not undo, and the panel takes no copy of the account as it
// stands. Both sentences are the difference between an informed decision and a surprised one, and
// both have been deleted from screens before for being long.
test('the dialog says a restore is not an undo and that no copy is taken first', async ({ page }) => {
  await openDialog(page)

  await expect(page.getByText('It is not an undo')).toBeVisible()
  await expect(page.getByText('No copy is taken of the account as it stands now')).toBeVisible()
  await expect(page.getByText('The point of no return is the first database that is dropped')).toBeVisible()
})

// The arm that matters most. `RestorePartial` means the account WAS changed, so the dialog must not
// offer the operation again: a blind second attempt over a half-replaced account is how the retry
// destroys what the first attempt left usable.
test('a partial restore reports a changed account and offers no way to try again', async ({
  page,
}) => {
  const backendDetail = 'The restore replaced part of the account and rolled the rest back.'
  await openDialog(page)
  await stubBackupRestore(page, 500, {
    code: 'RestorePartial',
    title: 'Restore failed',
    detail: backendDetail,
  })

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()

  // The backend's own already-localized text, verbatim (rules/vue.md).
  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByText('The account has been changed')).toBeVisible()
  // Positive control: the dialog is still on screen and one control IS offered.
  await expect(page.getByRole('button', { name: 'Close' }).last()).toBeVisible()

  await expect(page.getByRole('button', { name: 'Restore this account' })).toHaveCount(0)
  await expect(page.getByRole('textbox', { name: 'Account name' })).toHaveCount(0)
})

// The inverse control for the test above: every other code means nothing was touched, and a dialog
// that refused a retry on all of them would be as wrong as one that offered it on all of them.
test('a refused confirmation says nothing changed and keeps the form', async ({ page }) => {
  const backendDetail = 'That is not the name of the account this backup belongs to.'
  await openDialog(page)
  await stubBackupRestore(page, 409, {
    code: 'RestoreConfirmationMismatch',
    title: 'Restore refused',
    detail: backendDetail,
  })

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()

  await expect(page.getByText(backendDetail)).toBeVisible()
  // The instruction is the point, not the reassurance: THIS failure is the one a retype fixes, so
  // this is the only arm on which the screen may ask for one. Half of the pair below.
  await expect(page.getByText('Correct the confirmation and try again')).toBeVisible()
  await expect(page.getByText('The account has been changed')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Restore this account' })).toBeVisible()
  // The confirmation is spent once used: a filled field would put a second restore one click away.
  await expect(page.getByRole('textbox', { name: 'Account name' })).toHaveValue('')
})

// The counts are the server's answer and the sentence an operator needs; the SPA shows them and
// never recomputes the verdict they were derived from.
test('a whole restore reports the counts the panel gave back', async ({ page }) => {
  await openDialog(page)
  await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    whole: true,
    filesRestored: true,
    databasesRestored: 2,
    databasesTotal: 2,
    failureCode: '',
    failureDisplayName: '',
  })

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()

  await expect(page.getByText('alice was replaced')).toBeVisible()
  await expect(page.getByText('2 of 2 replaced')).toBeVisible()
})

// The witness for a mutation that survived: a mutant which PREFILLED the confirmation field left
// the "never prefilled" spec green, because the reset the dialog runs when it opens had never run
// at all — the page mounts the dialog already open, and a plain watcher does not fire for that. The
// visible half of the same defect is this one: the store's restore outcome outlived the dialog that
// produced it, so opening a second row met the first row's failure already on screen.
test('a dialog opened after a failed restore does not show the previous failure', async ({ page }) => {
  const backendDetail = 'That is not the name of the account this backup belongs to.'
  await openDialog(page)
  await stubBackupRestore(page, 409, {
    code: 'RestoreConfirmationMismatch',
    title: 'Restore refused',
    detail: backendDetail,
  })

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()
  await expect(page.getByText(backendDetail)).toBeVisible()

  await page.getByRole('button', { name: 'Cancel' }).click()
  await page.getByRole('button', { name: 'Actions for the backup of alice' }).click()
  await page.getByRole('menuitem', { name: 'Restore', exact: true }).click()

  // Positive control: the dialog IS open again, so its silence about the failure is a fact about
  // the dialog rather than about the probe.
  await expect(page.getByRole('textbox', { name: 'Account name' })).toBeVisible()
  await expect(page.getByText(backendDetail)).toHaveCount(0)
})

// The dialog's body is long because it has to be, and a panel that is taller than the window used
// to be centred and clipped by its own `overflow-hidden`: the footer — which is where the only
// control that can perform OR abandon the restore lives — was drawn below the fold with no
// scrollable ancestor to bring it back. The operator could read the warnings and type the name and
// then had nowhere to press. Every other spec in this file clicks the submit with `force: true`,
// which is exactly the actionability check that would have seen this, so nothing here observed it.
//
// The geometry, measured on this dialog at width 1280: the body's natural content is 778px, the
// whole panel 919px. At a 700px-tall window the clipped panel puts the confirmation field at
// y≈640 (on screen) and the submit at y≈750 (50px below the fold) — the smallest window that
// separates the two, so a failure here can only be the footer and never the field.
test('the confirm control of a dialog taller than the window can actually be pressed', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 700 })
  await openDialog(page)
  const submitted = await stubBackupRestore(page, 200, {
    backupId: COMPLETED.id,
    accountId: ALICE.id,
    whole: true,
    filesRestored: true,
    databasesRestored: 2,
    databasesTotal: 2,
    failureCode: '',
    failureDisplayName: '',
  })

  // Positive control, on the axis that the defect leaves untouched: the submit EXISTS. That is the
  // whole shape of this bug — the control was rendered, named and enabled, and unreachable — so a
  // presence check here is what makes the click below the only line that can distinguish the two
  // worlds, and it is deliberately not the assertion this spec rests on.
  const submit = page.getByRole('button', { name: 'Restore this account' })
  await expect(submit).toHaveCount(1)

  // Reaching the field is the other half of reachability: it sits at the bottom of the body, so it
  // is only fillable because the body is what scrolls.
  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')

  // THE CLAIM. No `force`: Playwright must scroll the footer into view and press a real, hit-
  // testable control, which is precisely what the clipped panel made impossible.
  await submit.click()

  await expect.poll(submitted).toHaveLength(1)
  expect(submitted()[0]).toEqual({ confirmAccountUsername: 'alice' })
})

// The other half of that pair, and the defect it was written for. The untouched arm answers two
// very different failures — a confirmation the server refused, and a copy the server could not use
// (a digest that did not match, a tampered or truncated artifact) — and it used to say "correct the
// confirmation and try again" for both. Retyping the account name has never repaired an artifact,
// so during a recovery the panel was sending the operator to the one action that cannot help.
// A spec that only ever fed a confirmation failure could not see it: both arms rendered the same
// sentence, so the check would have read the same on either side of the bug.
test('a copy the server could not use is not blamed on the operator typing', async ({ page }) => {
  const backendDetail = 'Your server rejected these details as invalid. Please correct them and try again.'
  await openDialog(page)
  await stubBackupRestore(page, 400, {
    code: 'AgentValidationFailed',
    title: 'Restore refused',
    detail: backendDetail,
  })

  await page.getByRole('textbox', { name: 'Account name' }).fill('alice')
  await page.getByRole('button', { name: 'Restore this account' }).click()

  // The backend's own already-localized text is still rendered verbatim (rules/vue.md).
  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByText('nothing was restored from this copy')).toBeVisible()
  // The dead end, gone: the panel must not tell this operator to retype anything.
  await expect(page.getByText('Correct the confirmation and try again')).toHaveCount(0)
  // Positive control on the probe: the form IS on screen and its controls readable here, so the
  // absence above is a fact about the copy this dialog rendered rather than about the locator.
  await expect(page.getByRole('button', { name: 'Restore this account' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Account name' })).toHaveValue('')
  // Nothing was touched, so this is NOT the changed-account arm.
  await expect(page.getByText('The account has been changed')).toHaveCount(0)
})
