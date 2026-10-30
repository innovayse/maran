import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'

// Issue #202: the sidebar offers a customer their own area and nothing that is the server's. The
// role is read from one place (`useCallerRole`), so this spec also guards that the sidebar did not
// keep a copy of its own.

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

const CUSTOMER_MODULES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
  { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
]

test('a customer sidebar carries no administrator entries', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, CUSTOMER_MODULES)

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation.getByRole('link', { name: 'Sites' }).first()).toBeVisible()

  for (const name of ['Audit journal', 'Plans', 'Grant repair', 'Schedule', 'Destinations', 'PHP versions']) {
    await expect(navigation.getByRole('link', { name })).toHaveCount(0)
  }
})

test('the not-available page has its own breadcrumb name', async ({ page }) => {
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubModules(page, CUSTOMER_MODULES)

  await page.goto('/not-available')

  // The breadcrumb must name the screen in words, never fall back to the route's machine name.
  await expect(page.locator('body')).not.toContainText('not-available')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
})

test('an administrator sidebar still offers the audit journal', async ({ page }) => {
  // Positive control for the absences above.
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, CUSTOMER_MODULES)

  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation.getByRole('link', { name: 'Audit journal' })).toBeVisible()
})
