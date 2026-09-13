import { expect, test, type Page } from '@playwright/test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubForgotPasswordAccepted } from '../fixtures/stub-password-reset-routes'

/**
 * Asks for a reset link for one address and returns everything the screen renders
 * afterwards, with the markup normalized only for things that cannot be equal
 * between two runs.
 * @param page The Playwright page to drive.
 * @param email The address to ask about.
 * @returns The rendered markup of the screen after the panel answered.
 */
const renderingAfterAsking = async (page: Page, email: string): Promise<string> => {
  await page.goto('/forgot-password')
  await page.getByLabel('Email').fill(email)
  await page.getByRole('button', { name: 'Send the link' }).click()

  const confirmation = page.getByText('If that address belongs to an account')
  await expect(confirmation).toBeVisible()

  // The whole rendered screen, not a chosen sentence: the point is that NOTHING
  // differs — not the wording, not the layout, not a control that is present in one
  // case and absent in the other.
  return page.locator('main').innerHTML()
}

test('a known and an unknown address are answered by the same screen, to the character', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  // One stub for both, because the endpoint answers both identically. The two calls
  // differ only in the address typed, which is the only variable under test.
  await stubForgotPasswordAccepted(page)

  const known = await renderingAfterAsking(page, 'admin@example.com')
  const unknown = await renderingAfterAsking(page, 'nobody-here@example.com')

  // Compared against EACH OTHER rather than against a fixed string: an assertion
  // that checked each separately against expected copy would still pass if the
  // wording changed in both, which is not what this spec is about. It would also
  // pass on empty markup, so the length is asserted too — a comparison of two empty
  // strings is a comparison of nothing.
  expect(known.length).toBeGreaterThan(0)
  expect(known).toBe(unknown)
})

test('asking for a link offers the way back to signing in', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubForgotPasswordAccepted(page)

  await page.goto('/forgot-password')
  await page.getByLabel('Email').fill('admin@example.com')
  await page.getByRole('button', { name: 'Send the link' }).click()
  await page.getByRole('button', { name: 'Back to sign in' }).click()

  await expect(page).toHaveURL('/login')
})

test('the sign-in screen links to the screen that asks for a reset link', async ({ page }) => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')
  await page.getByRole('button', { name: 'Forgot your password?' }).click()

  await expect(page).toHaveURL('/forgot-password')
})
