import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation } from '../fixtures/open-full-shell'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Page } from '@playwright/test'

// Issue #519, cross-screen state: feedback and data belong to the screen that produced them. This
// is the stubbed twin of e2e/real/cross-screen-state.spec.ts, and it runs under the network and
// console guard, so a read this form makes that no stub answers, or a console error, fails it too.

/**
 * Signs in as an administrator and opens the create-account form with every read it makes stubbed.
 * @param page The Playwright page under test.
 * @returns Resolves once the form is on screen.
 */
const openForm = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubShellReads(page)
  await stubAccounts(page, [])
  await stubPlans(page)
  await page.goto('/accounts/new')
  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
}

test('a validation message does not follow the administrator to the list and back', async ({ page }) => {
  await openForm(page)
  await page.getByRole('button', { name: 'Create account' }).click()
  // The control: the message was on screen, so its later absence means something.
  await expect(page.getByText('Username is required.')).toBeVisible()

  await mainNavigation(page).getByRole('link', { name: 'Accounts', exact: true }).first().click()
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible()
  await page.getByRole('link', { name: 'New account' }).first().click()

  await expect(page.getByRole('heading', { name: 'New account' })).toBeVisible()
  await expect(page.getByText('Username is required.')).toHaveCount(0)
})

test('a message drawn in English does not stay on screen when the language changes', async ({ page }) => {
  await setPersistedLocale(page, 'en')
  await openForm(page)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText('Username is required.')).toBeVisible()

  await page.getByRole('banner').getByTestId('locale-switcher').locator('[aria-haspopup="menu"]').click()
  await page.getByRole('menuitemradio', { name: 'Русский', exact: true }).click()

  await expect(page.getByText('Username is required.')).toHaveCount(0)
})
