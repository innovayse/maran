import { expect, test } from '../fixtures/guarded-test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { Page } from '@playwright/test'

/**
 * Signed-out pages must honour the persisted theme. The theme store used to be created only by the
 * authenticated shell, so login and its siblings rendered the baseline dark whatever was stored.
 * The sign-in screen renders before any request that matters to this check, but the panel is still
 * asked who is signed in, so the signed-out answers are stubbed and nothing is left unanswered.
 */

/**
 * Answers what a signed-out visit to the sign-in screen asks the panel.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once the routes are installed.
 */
const stubSignedOutVisit = async (page: Page): Promise<void> => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

for (const theme of ['light', 'dark'] as const) {
  test(`/login renders in the stored ${theme} theme`, async ({ page }) => {
    await stubSignedOutVisit(page)
    await page.addInitScript((stored: string) => {
      window.localStorage.setItem('maran.theme', stored)
    }, theme)

    await page.goto('/login')

    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
  })
}

test('/login follows the system preference when nothing is stored', async ({ page }) => {
  await stubSignedOutVisit(page)
  await page.emulateMedia({ colorScheme: 'light' })

  await page.goto('/login')

  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})
