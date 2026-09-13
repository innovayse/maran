import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

/** The account under test. */
const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

/** A panel that composed the Backups module, which is what makes a final copy possible. */
const WITH_BACKUPS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

/** The same panel composed without it — the `FinalBackupSkippedNoModule` case. */
const WITHOUT_BACKUPS: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true },
]

/**
 * Opens the account's detail page and starts recording the deletions the page sends — that a
 * request happened at all, because every test here is about WHEN one is allowed to leave.
 * @param page The Playwright page under test.
 * @param modules The catalogue the stubbed panel reports.
 * @returns The recorded DELETE urls, newest last.
 */
const openDetail = async (page: Page, modules: PanelModule[]): Promise<string[]> => {
  const deletes: string[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, modules)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
      return
    }

    if (route.request().method() === 'DELETE') {
      deletes.push(route.request().url())
      await route.fulfill({ status: 200, contentType: 'application/json', body: '4096' })
      return
    }

    await route.fallback()
  })
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  await page.goto(`/accounts/${ACCOUNT.id}`)
  return deletes
}

// The three facts the panel knew and the operator was not told: the copy is taken BEFORE anything
// is released, a copy that cannot be made refuses the deletion outright, and the copy stays behind
// afterwards. All three come from `DeleteAccountCommandHandler`'s step 0.
test('the deletion dialog says the final copy comes first, refuses on failure, and outlives the account', async ({
  page,
}) => {
  const deletes = await openDetail(page, WITH_BACKUPS)

  await page.getByRole('button', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('A final copy of the account is taken first')
  await expect(dialog).toContainText('the deletion is refused')
  await expect(dialog).toContainText('stays on this server after the account is gone')
  expect(deletes).toEqual([])
})

// Seven modules subscribe to `AccountDeleting`, and this dialog is the only place a person is told
// what one click is about to destroy. It listed the user, the home directory, the databases and the
// transfer logins, and said nothing about the sites it takes off the web server or the certificates
// that go with them — an incomplete list on an irreversible screen, which reads as exhaustive.
test('the deletion dialog names the sites and certificates it destroys, not only the files', async ({
  page,
}) => {
  await openDetail(page, WITH_BACKUPS)

  await page.getByRole('button', { name: 'Delete' }).click()

  const removed = page.getByRole('dialog').getByRole('list').first()
  // The home line is the control: it proves this locator is the rendered list rather than an empty
  // element that would satisfy any `toContainText` written below it by accident.
  await expect(removed).toContainText('The home directory and every file in it.')
  await expect(removed).toContainText('Every site this account hosts')
  await expect(removed).toContainText('certificates of those sites')
})

// A panel with no Backups module takes no copy, and the dialog must say the opposite thing rather
// than a softer version of the same one — implying a safety net that does not exist is the failure
// mode a destructive dialog cannot have.
test('a panel with no backups module says plainly that no final copy is taken', async ({ page }) => {
  await openDetail(page, WITHOUT_BACKUPS)

  await page.getByRole('button', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('no final copy is taken')
  await expect(dialog).not.toContainText('A final copy of the account is taken first')
})

// The bar the restore dialog set: a second click is satisfied by the same muscle that made the
// first one and by a mis-aimed row. Nothing may leave the browser until the account's own name has
// been typed, and a near-miss is a mis-read name.
test('a mistyped account name cannot delete the account', async ({ page }) => {
  const deletes = await openDetail(page, WITH_BACKUPS)

  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('textbox', { name: 'Type the account name to confirm' }).fill('ACME')

  await expect(page.getByRole('button', { name: 'Delete this account' })).toBeDisabled()
  expect(deletes).toEqual([])
})

// The discriminator, without which the two tests above would pass on a dialog that could never
// delete anything at all.
test('typing the account name exactly deletes it and returns to the list', async ({ page }) => {
  const deletes = await openDetail(page, WITH_BACKUPS)

  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('textbox', { name: 'Type the account name to confirm' }).fill(ACCOUNT.name)
  await page.getByRole('button', { name: 'Delete this account' }).click()

  await expect(page).toHaveURL('/accounts')
  await expect
    .poll(() => {
      return deletes
    })
    .toHaveLength(1)
  expect(deletes[0]).toContain(`/api/v1/accounts/${ACCOUNT.id}`)
})

// A refusal is the whole reason the dialog exists to be read: the panel's own message says why, and
// the account is still there. The dialog stays open, and the confirmation is spent.
test('a refused deletion shows the panel message and keeps the account', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, WITH_BACKUPS)
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT) })
      return
    }

    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        title: 'The account was not deleted: its final backup could not be made.',
        detail: 'The account was not deleted: its final backup could not be made.',
        code: 'FinalBackupFailed',
      }),
    })
  })
  await page.goto(`/accounts/${ACCOUNT.id}`)

  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('textbox', { name: 'Type the account name to confirm' }).fill(ACCOUNT.name)
  await page.getByRole('button', { name: 'Delete this account' }).click()

  // Scoped to the dialog: the page behind it renders the same store message in its own alert, and
  // the sentence has to be where the operator is looking — inside the dialog they are answering.
  await expect(
    page.getByRole('dialog').getByText('The account was not deleted: its final backup could not be made.'),
  ).toBeVisible()
  await expect(page).toHaveURL(`/accounts/${ACCOUNT.id}`)
  await expect(page.getByRole('button', { name: 'Delete this account' })).toBeDisabled()
})
