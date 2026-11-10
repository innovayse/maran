import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn, stubbedAdministrator } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSessions } from '../fixtures/stub-sessions-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Page } from '@playwright/test'

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
  await stubShellReads(page)
  await stubLandingReads(page, role === 'admin' ? 'administrator' : 'customer')
  await stubSessions(page, [])
  await stubAccounts(page, [])
  await stubAudit(page, [])
}

test('the account menu opens the sessions screen', async ({ page }) => {
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sessions' }).click()

  await expect(page).toHaveURL('/settings/sessions')
})

test('an administrator reaches the audit journal from the navigation', async ({ page }) => {
  // It used to be an account-menu entry, and this test used to click it there. The journal is a
  // server-wide screen rather than a setting about the person signed in, so it lives in the
  // navigation now — and it already had an entry there, which made the menu a second way to the
  // same screen under a second name. The claim worth keeping is that an administrator can reach
  // it, so the claim moved rather than the test being deleted. Since issue #311 it is a page of the
  // Monitoring section.
  await stubPanel(page, 'admin')

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Monitoring pages' }).click()
  await nav.getByRole('link', { name: 'Audit journal', exact: true }).click()

  await expect(page).toHaveURL('/settings/audit')
})

// A customer is not offered the audit journal: asserted in customer-area.spec.ts against the
// NAVIGATION, which is the only place that entry exists now. The spec that used to stand here
// looked for it in the account menu, and once the entry moved it could not fail for any role — a
// test that cannot fail is worse than no test, so it is not kept as reassurance.

test('the identity block is the only place the signed-in name appears, and it gets the footer width', async ({
  page,
}) => {
  // The 390px drawer is where the duplicate was fatal: a second control naming the same person
  // took half the footer and left the identity block 26px, enough for "r…" and "Adm".
  await page.setViewportSize({ width: 390, height: 844 })
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Open the navigation' }).click()

  const trigger = page.getByRole('button', { name: 'Account menu' })
  await expect(trigger).toBeVisible()
  // One control, not two: the name is inside the trigger and nowhere else in the footer.
  // Found by `data-testid`, not by a styling class: the hook names what the element is, so
  // restyling the card (or the line inside it) cannot silently turn this assertion into a no-op.
  await expect(
    page.getByTestId('shell-identity').getByText(stubbedAdministrator.username, { exact: true }),
  ).toHaveCount(1)

  const name = page.getByTestId('shell-identity-name')
  const width = await name.evaluate((element: HTMLElement): number => {
    return element.clientWidth
  })
  expect(width).toBeGreaterThan(100)
})

test('the account menu opens upwards when the sidebar footer leaves no room below', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Open the navigation' }).click()
  await page.getByRole('button', { name: 'Account menu' }).click()

  const menu = page.getByRole('menu')
  const box = await menu.boundingBox()
  const height = page.viewportSize()?.height ?? 0
  expect(box).not.toBeNull()
  expect(box?.y ?? -1).toBeGreaterThanOrEqual(0)
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(height)

  // Visible is not the same as usable: the point a user taps must land on the item itself.
  for (const label of ['Profile and security', 'Sessions', 'Two-step verification']) {
    const item = page.getByRole('menuitem', { name: label })
    const reachable = await item.evaluate((element: HTMLElement): boolean => {
      const rect = element.getBoundingClientRect()
      const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
      return hit !== null && element.contains(hit)
    })
    expect(reachable).toBe(true)
  }
})

test('the sidebar card ends the session from an icon-only button where the chevron was', async ({ page }) => {
  await stubPanel(page, 'admin')
  await page.route('**/api/v1/auth/logout', async (route) => {
    await route.fulfill({ status: 204 })
  })

  await page.goto('/')
  const sidebar = page.getByRole('complementary')
  const signOut = sidebar.getByRole('button', { name: 'Sign out', exact: true })
  await expect(signOut).toBeVisible()
  // Keyboard-reachable control with a tooltip, the icon being its only content.
  await expect(signOut).toHaveAttribute('title', 'Sign out')
  await expect(signOut).toHaveText('')

  await signOut.click()

  await expect(page).toHaveURL(/\/login/)
})

test('the sidebar card has exactly one sign-out control', async ({ page }) => {
  // Inverse control to the test above: the old full-width button must be gone, not merely joined by
  // a second one.
  await stubPanel(page, 'admin')

  await page.goto('/')
  const sidebar = page.getByRole('complementary')
  await expect(sidebar.getByRole('button', { name: /sign out/i })).toHaveCount(1)
})
