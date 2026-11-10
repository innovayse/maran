import { expect, test } from '../fixtures/guarded-test'
import {
  ALICE_ACCOUNT,
  EXPORT_ID,
  GRANT_TOKEN,
  SOURCE_FINGERPRINT,
  openAccountExport,
  openAccountMoveScreen,
} from '../fixtures/stub-account-move-routes'
import type { Page } from '@playwright/test'

// Moving an account between Maran servers, the export side (issue #127). Every screen is reached by
// clicking, from the sidebar down; the stub holds the panel's state, so a spec reads back what the page
// sent. The SPA's checks are advice (the panel enforces), so each refusing control has an inverse: the
// input it must accept.

/**
 * Exports alice from the export screen, the way a person does: choose the account, press the button.
 * @param page The Playwright page under test.
 * @returns Resolves once the package's screen is drawn.
 */
const exportAlice = async (page: Page): Promise<void> => {
  await openAccountExport(page)
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'alice' }).click()
  await page.getByRole('button', { name: 'Export account' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Export of alice' })).toBeVisible()
}

test.describe('the export form', () => {
  test('exports the chosen account and shows the key fingerprint to carry to the other server', async ({ page }) => {
    const state = await openAccountMoveScreen(page)

    await exportAlice(page)

    await expect(page.getByTestId('export-fingerprint')).toHaveText(SOURCE_FINGERPRINT)
    const made = state.requests.find((request) => {
      return request.method === 'POST' && request.url.pathname === '/api/v1/account-exports'
    })
    expect(made?.body).toEqual({ accountId: ALICE_ACCOUNT.id })
  })

  test('asks for an account before it asks the panel, and accepts one once chosen', async ({ page }) => {
    const state = await openAccountMoveScreen(page)
    await openAccountExport(page)

    await page.getByRole('button', { name: 'Export account' }).click()

    await expect(page.getByText('Choose the account to export.')).toBeVisible()
    expect(
      state.requests.filter((request) => {
        return request.method === 'POST'
      }),
    ).toHaveLength(0)

    // The inverse: with an account chosen the same control goes through.
    await page.getByRole('combobox', { name: 'Account' }).click()
    await page.getByRole('option', { name: 'alice' }).click()
    await page.getByRole('button', { name: 'Export account' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Export of alice' })).toBeVisible()
  })

  test("shows the panel's own words when it refuses the export", async ({ page, guard }) => {
    guard.allow(409, /\/api\/v1\/account-exports$/)
    await openAccountMoveScreen(page, {
      refusals: { 'POST account-exports': { detail: 'A backup of this account is already running.' } },
    })
    await openAccountExport(page)
    await page.getByRole('combobox', { name: 'Account' }).click()
    await page.getByRole('option', { name: 'alice' }).click()

    await page.getByRole('button', { name: 'Export account' }).click()

    await expect(page.getByText('A backup of this account is already running.')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: 'Export an account' })).toBeVisible()
  })
})

test.describe('the package', () => {
  test('saves the package under the name the panel proposes and says it is not encrypted', async ({ page }) => {
    await openAccountMoveScreen(page)
    await exportAlice(page)

    await expect(page.getByText('The package is not encrypted.', { exact: false })).toBeVisible()
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: 'Download package' }).click(),
    ])

    expect(download.suggestedFilename()).toBe('alice.maranpkg')
  })

  test('is the not-found state, not an alert, once the panel no longer has the export', async ({ page, guard }) => {
    // The refusal is the subject: the reload reads an export the panel no longer holds.
    guard.allow(404, /\/api\/v1\/account-exports\/[^/]+$/)
    const state = await openAccountMoveScreen(page)
    await exportAlice(page)
    state.exports = []

    await page.reload()

    await expect(page.getByText('This export is not available')).toBeVisible()
    await expect(page.getByRole('alert')).toHaveCount(0)
  })
})

test.describe('the pull grants', () => {
  test('shows the token once, in a dialog that cannot be dismissed by accident, and forgets it when closed', async ({ page }) => {
    const state = await openAccountMoveScreen(page)
    await exportAlice(page)

    await page.getByRole('textbox', { name: 'Allowed from (addresses or ranges)' }).fill('203.0.113.0/24')
    await page.getByRole('textbox', { name: 'Lifetime in minutes' }).fill('20')
    await page.getByRole('button', { name: 'Issue grant' }).click()

    const dialog = page.getByRole('dialog', { name: 'Pull grant issued' })
    await expect(dialog.getByTestId('grant-token')).toHaveText(GRANT_TOKEN)
    const issued = state.requests.find((request) => {
      return request.method === 'POST' && request.url.pathname === `/api/v1/account-exports/${EXPORT_ID}/grants`
    })
    expect(issued?.body).toEqual({ allowedFrom: '203.0.113.0/24', lifetimeMinutes: 20 })

    // Escape must not destroy the only copy of the token.
    await page.keyboard.press('Escape')
    await expect(dialog).toBeVisible()

    await dialog.getByRole('button', { name: 'I have copied it' }).click()
    await expect(dialog).toHaveCount(0)
    // The positive control above proves the search can find the token; here it must find nothing.
    await expect(page.getByText(GRANT_TOKEN)).toHaveCount(0)
    // The grant itself is read back from the panel on the grants page, never kept on this screen.
    await expect(page.getByRole('link', { name: 'See the grants of alice' })).toBeVisible()
  })

  test('refuses an unusable address range before asking the panel, and accepts a range and a single address', async ({ page }) => {
    const state = await openAccountMoveScreen(page)
    await exportAlice(page)
    const issueRequests = (): number => {
      return state.requests.filter((request) => {
        return request.method === 'POST' && request.url.pathname.endsWith('/grants')
      }).length
    }

    await page.getByRole('textbox', { name: 'Allowed from (addresses or ranges)' }).fill('not-an-address')
    await page.getByRole('button', { name: 'Issue grant' }).click()
    await expect(page.getByText('Enter addresses or ranges such as', { exact: false })).toBeVisible()
    expect(issueRequests()).toBe(0)

    // The inverse: a single address and a range, separated by a comma, go through.
    await page.getByRole('textbox', { name: 'Allowed from (addresses or ranges)' }).fill('198.51.100.7, 203.0.113.0/24')
    await page.getByRole('button', { name: 'Issue grant' }).click()
    await expect(page.getByRole('dialog', { name: 'Pull grant issued' })).toBeVisible()
    expect(issueRequests()).toBe(1)
  })

  test('refuses a lifetime above the cap before asking the panel', async ({ page }) => {
    const state = await openAccountMoveScreen(page)
    await exportAlice(page)

    await page.getByRole('textbox', { name: 'Lifetime in minutes' }).fill('90')
    await page.getByRole('button', { name: 'Issue grant' }).click()

    await expect(page.getByText('Enter a whole number of minutes from 1 to 60.')).toBeVisible()
    expect(
      state.requests.filter((request) => {
        return request.url.pathname.endsWith('/grants')
      }),
    ).toHaveLength(0)
  })
})
