import { expect, test, type Page } from '@playwright/test'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'

// The two most destructive dialogs in the product were not operable from a keyboard at all.
//
// Both are mounted with `v-if` — their props are only valid while they are open — and given `:open`
// at the same time, so `UiModal` was created with `open` already `true` and its focus watcher, which
// had no `immediate`, never fired for either of them. Measured in a live browser: focus stayed on
// the page behind, Escape did NOTHING (the key handler is a `keydown` on the backdrop, so a
// keystroke that never originates inside the dialog never reaches it), and dismissing any other way
// dropped focus to `<body>` — a keyboard user returned to the top of the document in front of a
// page whose destructive button they must Tab all the way back to.
//
// So these specs press Escape and assert the dialog CLOSED and focus RETURNED to the control that
// opened it. A spec asserting the dialog merely renders passes against every one of those defects.
//
// Asserted in Russian, the language the panel comes up in out of the box and the one the defect was
// found in, so a dialog identified by an untranslated accessible name fails here too.

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

/** A completed copy — the only state a restore may be offered from. */
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
 * Reports whether the browser's focus currently sits on an element inside the open dialog.
 *
 * Read from `document.activeElement` rather than from a Playwright `:focus` locator, because the
 * defect's signature is focus being somewhere ELSE entirely — on the page behind, or on `<body>` —
 * and only the document's own answer distinguishes those from "nothing matched".
 * @param page The page under test.
 * @returns True when the active element is the dialog panel or a descendant of it.
 */
const focusIsInsideDialog = (page: Page): Promise<boolean> => {
  return page.evaluate(() => {
    const dialog = document.querySelector('[role="dialog"]')
    const active = document.activeElement
    return dialog !== null && active !== null && dialog.contains(active)
  })
}

/**
 * Reports the accessible name of whatever currently has focus, for asserting where focus LANDED.
 * @param page The page under test.
 * @returns The active element's trimmed text, its `aria-label`, or its tag name for `<body>`.
 */
const focusedName = (page: Page): Promise<string> => {
  return page.evaluate(() => {
    const active = document.activeElement
    if (active === null) {
      return 'null'
    }
    if (active === document.body) {
      return 'BODY'
    }
    return active.getAttribute('aria-label') ?? (active.textContent ?? '').trim()
  })
}

test('the account-deletion dialog takes focus, closes on Escape, and gives focus back', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'accounts', displayName: 'Аккаунты', tier: 'included', isEnabled: true },
  ])
  await page.route(`**/api/v1/accounts/${ALICE.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ALICE) })
  })

  await page.goto(`/accounts/${ALICE.id}`)
  await page.getByRole('button', { name: 'Удалить' }).click()

  const dialog = page.getByRole('dialog', { name: 'Удаление alice' })
  await expect(dialog).toBeVisible()
  expect(await focusIsInsideDialog(page)).toBe(true)

  // Escape from wherever the dialog put focus — nothing is clicked first. Clicking into the field
  // is what masked this defect during the live pass: it moved focus inside by hand, and Escape then
  // worked, so the dialog looked operable.
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toHaveCount(0)
  // The value, not a bound: focus is on the button that opened the dialog, not merely "not BODY".
  expect(await focusedName(page)).toBe('Удалить')
})

test('the backup-restore dialog takes focus, closes on Escape, and gives focus back', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    { name: 'backups', displayName: 'Резервные копии', tier: 'included', isEnabled: true },
  ])
  await stubAccounts(page, [ALICE])
  await stubBackups(page, [COMPLETED])

  await page.goto('/backups')
  await page.getByRole('button', { name: 'Действия для резервной копии alice' }).click()
  await page.getByRole('menuitem', { name: 'Восстановить', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Восстановление alice' })
  await expect(dialog).toBeVisible()
  expect(await focusIsInsideDialog(page)).toBe(true)

  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toHaveCount(0)
  // The row's menu trigger, which is where the dialog was opened from — the control a keyboard user
  // must be returned to in order to carry on down the table.
  expect(await focusedName(page)).toBe('Действия для резервной копии alice')
})
