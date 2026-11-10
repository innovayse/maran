import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE } from '../fixtures/open-full-shell'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyReseller } from '../fixtures/stub-reseller-session'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuthenticatedUser } from '../../src/types/auth'

// The phone tab bar's Home is the role's own landing entry, not a fixed key (issue #179).

/** A reseller: signed in with no hosting account of its own. */
const RESELLER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000a1',
  username: 'reseller',
  email: 'reseller@example.com',
  role: 'reseller',
  accountId: null,
}

test('on a phone the reseller bar starts with a Home tab that opens the reseller overview', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page, RESELLER)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubShellReads(page)
  // The landing screen sends a reseller to its own overview, which reads the reseller's envelope and the plan features.
  await stubMyReseller(page)
  await page.goto('/')

  const bar = page.getByRole('navigation', { name: 'Sections' })
  const home = bar.getByRole('link').first()

  await expect(home).toHaveText('Home')
  await expect(home).toHaveAttribute('href', '/reseller')
})

test('on a phone the administrator Home tab still opens the server dashboard', async ({ page }) => {
  // Inverse control: the reseller's landing is chosen by role, not applied to everyone.
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubShellReads(page)
  await stubLandingReads(page)
  await page.goto('/')

  const home = page.getByRole('navigation', { name: 'Sections' }).getByRole('link').first()

  await expect(home).toHaveText('Home')
  await expect(home).toHaveAttribute('href', '/')
})
