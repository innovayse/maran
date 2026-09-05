import { expect, test } from '@playwright/test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubResetPasswordRefused } from '../fixtures/stub-password-reset-routes'

/** The panel's single refusal, worded so it says nothing about which token this was. */
const REFUSAL = 'This reset link is no longer valid. Ask for a new one.'

test('a spent or expired token shows the panel’s refusal and a way back', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubResetPasswordRefused(page, REFUSAL)

  await page.goto('/reset-password?token=already-spent')
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
  await page.getByLabel('Confirm password').fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Set the password' }).click()

  await expect(page.getByText(REFUSAL)).toBeVisible()

  // The way back. Without it the person is left on a dead screen holding a stale
  // link, with the sign-in page reachable only by editing the URL.
  await page.getByRole('button', { name: 'Back to sign in' }).click()
  await expect(page).toHaveURL('/login')
})

test('the new-password field offers to generate one', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/reset-password?token=fresh')
  await page.getByRole('button', { name: 'Generate a password' }).click()

  // A secret is being MINTED here, unlike the mail-settings screen where an
  // existing provider credential is entered — which is why only this field has it.
  await expect(page.getByLabel('Password', { exact: true })).not.toHaveValue('')
})
