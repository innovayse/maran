import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubResellerSession } from '../fixtures/stub-reseller-session'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSessions } from '../fixtures/stub-sessions-route'
import { stubAudit } from '../fixtures/stub-audit-routes'
import type { Locator, Page } from '@playwright/test'

/** The three roles the menu is checked for. */
type Role = 'admin' | 'reseller' | 'customer'

/**
 * Signs a person in with the given role and answers the pages the menu leads to.
 * @param page The Playwright page whose network the routes are installed on.
 * @param role The role the panel reports for the signed-in user.
 * @returns Resolves once the routes are installed.
 */
const stubPanel = async (page: Page, role: Role): Promise<void> => {
  if (role === 'reseller') {
    // A reseller lands on their own overview, which reads their envelope and the plan features.
    await stubResellerSession(page)
  } else {
    await stubSignedIn(page, { ...stubbedAdministrator, role })
    await stubHealthy(page)
    await stubEmptyModules(page)
    await stubShellReads(page)
    await stubLandingReads(page, role === 'admin' ? 'administrator' : 'customer')
  }
  await stubSessions(page, [])
  await stubAudit(page, [])
  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 204 })
  })
}

/**
 * The app bar's account-menu trigger (the avatar).
 * @param page The page under test.
 * @returns The trigger locator.
 */
const headerTrigger = (page: Page): Locator => {
  return page.getByRole('button', { name: 'Your account' })
}

for (const role of ['admin', 'reseller', 'customer'] as const) {
  test(`the header avatar opens the account menu for ${role} by click`, async ({ page }) => {
    await stubPanel(page, role)
    await page.goto('/')

    const trigger = headerTrigger(page)
    await expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    await expect(trigger).toHaveAttribute('aria-expanded', 'false')
    await trigger.click()

    await expect(trigger).toHaveAttribute('aria-expanded', 'true')
    for (const label of ['Profile and security', 'Sessions', 'Two-step verification', 'Sign out']) {
      await expect(page.getByRole('menuitem', { name: label })).toBeVisible()
    }
  })
}

test('the header avatar opens the account menu from the keyboard and Escape returns focus', async ({
  page,
}) => {
  await stubPanel(page, 'admin')
  await page.goto('/')

  const trigger = headerTrigger(page)
  await trigger.focus()
  await page.keyboard.press('Enter')

  await expect(page.getByRole('menu')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Profile and security' })).toBeFocused()

  await page.keyboard.press('Escape')
  await expect(page.getByRole('menu')).toHaveCount(0)
  await expect(trigger).toBeFocused()
})

test('a click outside closes the header account menu', async ({ page }) => {
  await stubPanel(page, 'admin')
  await page.goto('/')

  await headerTrigger(page).click()
  await expect(page.getByRole('menu')).toBeVisible()

  await page.mouse.click(5, 400)
  await expect(page.getByRole('menu')).toHaveCount(0)
})

test('a header menu entry leads to its page', async ({ page }) => {
  await stubPanel(page, 'customer')
  await page.goto('/')

  await headerTrigger(page).click()
  await page.getByRole('menuitem', { name: 'Sessions' }).click()

  await expect(page).toHaveURL('/settings/sessions')
})

test('signing out from the header menu ends the session and leaves the panel', async ({ page }) => {
  await stubPanel(page, 'admin')
  await page.goto('/')

  await headerTrigger(page).click()
  const logout = page.waitForRequest('**/api/v1/auth/logout')
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await logout

  await expect(page).toHaveURL(/\/login/)
})

test('the header avatar is hidden on a narrow bar', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubPanel(page, 'admin')
  await page.goto('/')

  await expect(headerTrigger(page)).toBeHidden()
})

test('the sidebar menu still opens and carries no second Sign out row', async ({ page }) => {
  // Inverse control: the shared menu in the sidebar is unchanged, and the sign-out row belongs to
  // the header's copy only (the sidebar card has its own button).
  await stubPanel(page, 'admin')
  await page.goto('/')

  await page.getByRole('button', { name: 'Account menu' }).click()
  await expect(page.getByRole('menuitem', { name: 'Profile and security' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Sign out' })).toHaveCount(0)
  await page.getByRole('menuitem', { name: 'Two-step verification' }).click()
  await expect(page).toHaveURL('/settings/two-factor')
})
