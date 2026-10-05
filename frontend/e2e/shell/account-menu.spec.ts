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

test('an administrator reaches the audit journal from the navigation', async ({ page }) => {
  // It used to be an account-menu entry, and this test used to click it there. The journal is a
  // server-wide screen rather than a setting about the person signed in, so it lives in the
  // navigation now — and it already had an entry there, which made the menu a second way to the
  // same screen under a second name. The claim worth keeping is that an administrator can reach
  // it, so the claim moved rather than the test being deleted.
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('link', { name: 'Audit journal' }).click()

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
  await expect(page.locator('.shell-footer').getByText(stubbedAdministrator.username, { exact: true })).toHaveCount(1)

  const name = trigger.locator('span.truncate').first()
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
  for (const label of ['Sessions', 'Two-step verification', 'Sign out']) {
    const item = page.getByRole('menuitem', { name: label })
    const reachable = await item.evaluate((element: HTMLElement): boolean => {
      const rect = element.getBoundingClientRect()
      const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
      return hit !== null && element.contains(hit)
    })
    expect(reachable).toBe(true)
  }
})
