import { expect, test, type Page } from '@playwright/test'
import { stubSignedOut } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'

// Issue #149: a reader who asked for reduced motion gets every transition finished at once. The
// sign-in button is the probe because it carries the panel's longest transition, the 700ms spring
// of `transition-button`.

/**
 * Opens the sign-in screen signed out.
 * @param page The Playwright page under test.
 * @returns Resolves once the form is drawn.
 */
const openSignIn = async (page: Page): Promise<void> => {
  await stubSignedOut(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await page.goto('/login')
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
}

/**
 * Reads the computed transition durations of a button.
 * @param page The Playwright page under test.
 * @param name The accessible name of the button to read.
 * @returns The `transition-duration` value the browser computed.
 */
const transitionDurationOf = async (page: Page, name: string): Promise<string> => {
  return page.getByRole('button', { name, exact: true }).evaluate((element) => {
    return getComputedStyle(element).transitionDuration
  })
}

test.describe('with reduced motion requested', () => {
  test.use({ reducedMotion: 'reduce' })

  test('every transition on the sign-in screen finishes on its first frame', async ({ page }) => {
    await openSignIn(page)

    const durations = (await transitionDurationOf(page, 'Sign in')).split(',').map((value) => {return value.trim()})
    expect(durations.every((value) => {return value === '0s'})).toBe(true)
  })
})

test.describe('with no motion preference', () => {
  test.use({ reducedMotion: 'no-preference' })

  test('the sign-in button keeps its spring', async ({ page }) => {
    // The inverse control: a rule that zeroed transitions for everyone would pass the test above.
    await openSignIn(page)

    expect(await transitionDurationOf(page, 'Sign in')).toContain('0.7s')
  })
})
