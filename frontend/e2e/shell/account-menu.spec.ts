import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

/**
 * Signs a person in with the given role and answers the pages the menu leads to.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns Resolves once the routes are installed.
 */
const stubPanel = async (page: Page, role: 'admin' | 'customer'): Promise<void> => {
  await stubSignedIn(page, { ...stubbedAdministrator, role })
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/sessions', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  await page.route('**/api/v1/audit*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

test('the account menu opens the sessions screen', async ({ page }) => {
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sessions' }).click()

  await expect(page).toHaveURL('/settings/sessions')
})

test('the account menu opens the audit journal for an administrator', async ({ page }) => {
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Audit journal' }).click()

  await expect(page).toHaveURL('/settings/audit')
})

test('a customer is not offered the audit journal', async ({ page }) => {
  // Presentation, not authorization: the endpoint refuses a customer whatever the menu shows.
  // A link that only ever answers 403 is a worse answer than no link.
  await stubPanel(page, 'customer')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()

  await expect(page.getByRole('menuitem', { name: 'Sessions' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Audit journal' })).toHaveCount(0)
})
