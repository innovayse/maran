import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'

/** The account under test, active to begin with. */
const ACTIVE: Account = {
  id: '00000000-0000-0000-0000-0000000000c1',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '00000000-0000-0000-0000-0000000000d1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

/** The same account after the panel has suspended it. */
const SUSPENDED: Account = { ...ACTIVE, status: 'suspended' }

/**
 * Puts the accounts module in the catalogue and answers the detail read.
 * @param page The Playwright page whose network the routes are installed on.
 * @param account The account the panel reports.
 * @returns Resolves once the routes are installed.
 */
const stubDetail = async (page: Page, account: Account): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }])
  await page.route(`**/api/v1/accounts/${account.id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(account) })
  })
}

test('the detail page shows the account and offers suspension while it is active', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)

  await expect(page.getByText('acme.example.com').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toHaveCount(0)
})

test('suspending asks in a dialog that names the account and what will happen', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()

  // The dialog's accessible name says WHICH account: a generic "Confirm" would read
  // the same to a screen-reader user whichever row they came from.
  await expect(page.getByRole('dialog', { name: 'Suspend account acme' })).toBeVisible()
  // Not "are you sure": the operator is being asked to weigh a consequence their
  // customer sees within seconds.
  await expect(page.getByRole('dialog')).toContainText(
    'Its sites stop serving and its user can no longer sign in.',
  )
})

// A modal whose default answer is "yes" is weaker than the sentence it replaced, so the
// focused control on open must not be the one that suspends the account.
test('the confirmation does not open with the confirming button focused', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Yes, do it' })).not.toBeFocused()
  // Focus is inside the dialog all the same — a trap around nothing is not a trap.
  await expect(dialog.locator(':focus')).toHaveCount(1)
})

// Escape is a dismissal, and a dismissal must not be an answer.
test('escaping the confirmation suspends nothing and gives the button its focus back', async ({
  page,
}) => {
  await stubDetail(page, ACTIVE)
  let suspendRequests = 0
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    suspendRequests += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SUSPENDED) })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(suspendRequests).toBe(0)
  // Back on the control that opened it, not at the top of the document.
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeFocused()
  await expect(page.getByText('Active')).toBeVisible()
})

test('a confirmed suspension shows the new state without a reload', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SUSPENDED) })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText('Suspended')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible()
})

test('a refused action shows the panel message verbatim and changes nothing', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route(`**/api/v1/accounts/${ACTIVE.id}/suspend`, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'The agent is unavailable.', detail: 'The agent is unavailable.', code: 'AgentUnavailable' }),
    })
  })

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Suspend' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByText('The agent is unavailable.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Suspend' })).toBeVisible()
})

// Deleting is confirmed by TYPING the account's name — e2e/accounts/delete.spec.ts proves that
// gesture and what the dialog says. What belongs here is that this page opens that dialog and not
// the yes/no one the two reversible actions use: both are modals now, so "a dialog appeared" no
// longer distinguishes them and the field is what does.
test('the delete button opens the typed confirmation, not the yes/no one', async ({ page }) => {
  await stubDetail(page, ACTIVE)

  await page.goto(`/accounts/${ACTIVE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByLabel('Type the account name to confirm')).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Yes, do it' })).toHaveCount(0)
})

test('the list opens an account by its name', async ({ page }) => {
  await stubDetail(page, ACTIVE)
  await page.route('**/api/v1/accounts', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([ACTIVE]) })
  })

  await page.goto('/accounts')
  await page.getByRole('link', { name: 'acme' }).click()

  await expect(page).toHaveURL(`/accounts/${ACTIVE.id}`)
})
