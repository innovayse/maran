import { expect, test, type Page } from '@playwright/test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { setPersistedLocale } from '../fixtures/set-locale'

// Issue #202 item 5: the accept-invitation response carries `{ username }`, the login the
// invitation created, and the success screen names it so the person knows what to sign in with.

const TOKEN = 'invitation-xyz789'

const fillAndSubmit = async (page: Page): Promise<void> => {
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
  await page.getByLabel('Confirm password').fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Set the password' }).click()
}

test('the success screen names the username the invitation created', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await page.route('**/api/v1/auth/accept-invitation', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ username: 'olive' }) })
  })

  await page.goto(`/accept-invitation?token=${TOKEN}`)
  await fillAndSubmit(page)

  await expect(page.getByText('Your password has been set.')).toBeVisible()
  await expect(page.getByText('Your username is olive.')).toBeVisible()
})

test('the username line is localized', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await stubSignedOut(page)
  await stubHealthy(page)
  await page.route('**/api/v1/auth/accept-invitation', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ username: 'olive' }) })
  })

  await page.goto(`/accept-invitation?token=${TOKEN}`)
  await page.locator('input[autocomplete="new-password"]').nth(0).fill('correct horse battery staple')
  await page.locator('input[autocomplete="new-password"]').nth(1).fill('correct horse battery staple')
  await page.locator('button[type="submit"]').click()

  await expect(page.getByText('Ձեր օգտանունն է՝ olive։')).toBeVisible()
})
