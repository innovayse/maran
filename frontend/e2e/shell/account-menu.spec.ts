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

test('the account menu opens the audit journal for an administrator', async ({ page }) => {
  await stubPanel(page, 'admin')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Audit journal' }).click()

  await expect(page).toHaveURL('/settings/audit')
})

test('a customer is not offered the audit journal', async ({ page }) => {
  // Presentation, not authorization: the endpoint refuses a customer whatever the menu shows.
  // A link that only ever answers 403 is a worse answer than no link.
  await stubPanel(page, 'customer')

  await page.goto('/')
  await page.getByRole('button', { name: 'Account menu' }).click()

  await expect(page.getByRole('menuitem', { name: 'Sessions' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Audit journal' })).toHaveCount(0)
})

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
  for (const label of ['Sessions', 'Two-step verification', 'Audit journal', 'Sign out']) {
    const item = page.getByRole('menuitem', { name: label })
    const reachable = await item.evaluate((element: HTMLElement): boolean => {
      const rect = element.getBoundingClientRect()
      const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
      return hit !== null && element.contains(hit)
    })
    expect(reachable).toBe(true)
  }
})
