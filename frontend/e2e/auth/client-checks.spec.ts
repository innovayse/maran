import { expect, test, type Page } from '@playwright/test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

// Issue #213: the signed-out forms catch what can never succeed before a request leaves the
// browser. These checks are advice only — the backend re-validates every one of them — so each
// spec counts the requests that reached the endpoint instead of trusting the screen alone.
// Issue #221 (point 3): a refusal left by one signed-out screen does not follow the visitor to
// the sign-in screen.

/**
 * Installs the session, health and catalogue answers every signed-out screen needs.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubSignedOutShell = async (page: Page): Promise<void> => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/**
 * Counts the requests made to one endpoint, answering each with a refusal so a request that
 * should not have been made cannot silently succeed either.
 * @param page The Playwright page whose network the route is installed on.
 * @param pattern The endpoint's URL pattern.
 * @returns A reader for the number of requests seen so far.
 */
const countRequests = async (page: Page, pattern: string): Promise<() => number> => {
  let seen = 0
  await page.route(pattern, async (route) => {
    seen += 1
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Refused', detail: 'Refused by the stub.' }),
    })
  })
  return (): number => {return seen}
}

test('an empty sign-in marks both fields required and sends nothing', async ({ page }) => {
  await stubSignedOutShell(page)
  const logins = await countRequests(page, '**/api/v1/auth/login**')

  await page.goto('/login')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await expect(page.getByRole('textbox', { name: 'Username' })).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByRole('textbox', { name: 'Password' })).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByText('Enter your username.')).toBeVisible()
  await expect(page.getByText('Enter your password.')).toBeVisible()
  expect(logins()).toBe(0)
})

test('an empty reset request marks the address required and sends nothing', async ({ page }) => {
  await stubSignedOutShell(page)
  const requests = await countRequests(page, '**/api/v1/auth/forgot-password')

  await page.goto('/forgot-password')
  await page.getByRole('button', { name: 'Send the link' }).click()

  await expect(page.getByRole('textbox', { name: 'Email' })).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByText('Enter your email address.')).toBeVisible()
  expect(requests()).toBe(0)
})

test('a reset link without a token states that it is invalid and offers no form', async ({ page }) => {
  await stubSignedOutShell(page)

  await page.goto('/reset-password')

  await expect(page.getByText('This reset link is invalid')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Set the password' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Ask for a new link' })).toBeVisible()
})

test('an invitation link without a token states that it is incomplete and offers no form', async ({ page }) => {
  await stubSignedOutShell(page)

  await page.goto('/accept-invitation')

  await expect(page.getByText('This invitation link is incomplete')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Set the password' })).toHaveCount(0)
})

test('a short invitation password is flagged once before it is sent', async ({ page }) => {
  await stubSignedOutShell(page)
  const accepts = await countRequests(page, '**/api/v1/auth/accept-invitation')

  await page.goto('/accept-invitation?token=fresh')
  await page.getByLabel('Password', { exact: true }).fill('short')
  await page.getByLabel('Confirm password').fill('short')
  await page.getByRole('button', { name: 'Set the password' }).click()

  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByText('Use at least 12 characters.')).toBeVisible()
  expect(accepts()).toBe(0)

  // Advice, not a rule: the operator may have lowered the minimum, so a second press sends it and
  // the server's answer decides.
  await page.getByRole('button', { name: 'Set the password' }).click()
  await expect(page.getByText('Refused by the stub.')).toBeVisible()
  expect(accepts()).toBe(1)
})

test('a refused invitation does not carry its error to the sign-in screen', async ({ page }) => {
  await stubSignedOutShell(page)
  await countRequests(page, '**/api/v1/auth/accept-invitation')

  await page.goto('/accept-invitation?token=spent')
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
  await page.getByLabel('Confirm password').fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Set the password' }).click()
  await expect(page.getByText('Refused by the stub.')).toBeVisible()

  await page.getByRole('link', { name: 'Back to sign in' }).click()
  await expect(page).toHaveURL('/login')

  // Positive control: the sign-in screen rendered, so "no refusal" is not an empty page passing.
  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
  await expect(page.getByText('Refused by the stub.')).toHaveCount(0)
})

test('a refused reset link does not carry its error to the sign-in screen', async ({ page }) => {
  await stubSignedOutShell(page)
  await countRequests(page, '**/api/v1/auth/reset-password')

  await page.goto('/reset-password?token=spent')
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery staple')
  await page.getByLabel('Confirm password').fill('correct horse battery staple')
  await page.getByRole('button', { name: 'Set the password' }).click()
  await expect(page.getByText('Refused by the stub.')).toBeVisible()

  await page.getByRole('link', { name: 'Back to sign in' }).click()
  await expect(page).toHaveURL('/login')

  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
  await expect(page.getByText('Refused by the stub.')).toHaveCount(0)
})
