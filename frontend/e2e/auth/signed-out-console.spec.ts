import { expect, test, type ConsoleMessage, type Page } from '@playwright/test'
import { stubSetupState, stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'

// Issue #204, item 5: a signed-out page load asks `/auth/refresh` once, and with no refresh cookie
// the backend answers 204 rather than 401 — so the sign-in screen opens with a quiet console.

/**
 * Collects every console error the page logs from now on.
 * @param page The page to listen on.
 * @returns The live list the listener appends to.
 */
const collectConsoleErrors = (page: Page): string[] => {
  const errors: string[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') {
      errors.push(message.text())
    }
  })
  return errors
}

test('a signed-out visit to the sign-in screen with no cookies leaves the console clean', async ({ page }) => {
  await page.context().clearCookies()
  const consoleErrors = collectConsoleErrors(page)
  const refreshStatuses: number[] = []
  page.on('response', (response) => {
    if (response.url().endsWith('/api/v1/auth/refresh')) {
      refreshStatuses.push(response.status())
    }
  })
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)

  await page.goto('/login')

  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
  expect(refreshStatuses).toEqual([204])
  expect(consoleErrors).toEqual([])
})

// Issue #218, point 11: every signed-out screen, not only the sign-in one, opens without a console
// error — the refresh every page load asks answers 204, and no screen asks anything whose expected
// answer is a 4xx the browser would log.
for (const path of ['/login', '/forgot-password', '/reset-password?token=abc', '/accept-invitation?token=abc', '/setup']) {
  test(`the signed-out screen ${path} opens with a quiet console`, async ({ page }) => {
    await page.context().clearCookies()
    const consoleErrors = collectConsoleErrors(page)
    await stubSignedOut(page)
    await stubHealthy(page)
    await stubEmptyModules(page)
    await stubSetupState(page, { isComplete: false })

    await page.goto(path)

    await expect(page.getByRole('heading', { level: 2 }).first()).toBeVisible()
    expect(consoleErrors).toEqual([])
  })
}

test('a refused refresh cookie still lands on the sign-in screen and is seen by the console probe', async ({
  page,
}) => {
  // The inverse control: a cookie that was sent and refused still answers 401, and the probe above
  // must be able to see the console error that 401 produces — otherwise its empty list proves nothing.
  const consoleErrors = collectConsoleErrors(page)
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.route('**/api/v1/auth/refresh', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RefreshTokenInvalidUnauthorized', detail: 'Your session has ended.' }),
    })
  })

  await page.goto('/login')

  await expect(page.getByRole('heading', { level: 2, name: 'Sign in' })).toBeVisible()
  await expect.poll(() => {return consoleErrors.length}).toBeGreaterThan(0)
})
