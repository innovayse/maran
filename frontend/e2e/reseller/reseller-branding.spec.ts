import { expect, test, type Page } from '@playwright/test'
import { Buffer } from 'node:buffer'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import {
  NO_BRANDING,
  TINY_PNG,
  stubResellerBranding,
  stubShellBranding,
  type ResellerBrandingScript,
} from '../fixtures/stub-branding-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { MyAccount } from '../../src/types/account'

// Issue #367: a reseller sets a name, a logo and an accent colour, and its customers' shell wears
// them. The panel stores and validates every value; the SPA sends what was typed and renders what the
// panel answers. Each test enters through one `goto`; the rest is clicks.

/** A customer of the reseller. */
const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: '11111111-1111-1111-1111-111111111111',
}

/** The customer's own account. */
const OWN_ACCOUNT: MyAccount = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'olive',
  primaryDomain: 'olive-shop.example.com',
  status: 'active',
  statusName: 'Active',
  plan: { displayName: 'Starter', diskQuotaMb: 1024, maxSites: 1, maxDatabases: 1, maxSftpUsers: 1, maxFtpUsers: 1, maxCronEntries: 1 },
}

/**
 * Signs the reseller in, stubs its branding endpoint and opens the Branding page from the sidebar.
 * @param page The Playwright page under test.
 * @param script The saves received and the refusal to answer with.
 * @returns Resolves once the page's heading is drawn.
 */
const openBrandingPage = async (page: Page, script: ResellerBrandingScript): Promise<void> => {
  await stubResellerSession(page, undefined, RESELLER_MODULES)
  await stubResellerBranding(page, script)
  await page.goto('/')
  await openSection(page, 'Settings')
  await mainNavigation(page).getByRole('link', { name: 'Branding', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Branding' })).toBeVisible()
}

test('a reseller saves its name, colour and logo, and the form shows what the panel stored', async ({ page }) => {
  const script: ResellerBrandingScript = { saved: [] }
  await openBrandingPage(page, script)

  await page.getByLabel('Name shown to customers').fill('Alpha Hosting')
  await page.getByLabel('Colour (#rrggbb)').fill('#2468DC')
  await page.getByLabel('Choose a logo').setInputFiles({
    name: 'logo.png',
    mimeType: 'image/png',
    // A Buffer, not a bare Uint8Array: the file payload is sent as a Buffer's base64.
    buffer: Buffer.from(TINY_PNG.slice(TINY_PNG.indexOf(',') + 1), 'base64'),
  })
  await expect(page.getByRole('button', { name: 'Remove the logo' })).toBeVisible()
  await page.getByRole('button', { name: 'Save branding' }).click()

  await expect(page.getByText('The branding has been saved.', { exact: false })).toBeVisible()
  expect(script.saved).toEqual([{ displayName: 'Alpha Hosting', accentColor: '#2468DC', logo: TINY_PNG }])
  // The panel normalised the colour to lower case, and the form follows the stored value.
  await expect(page.getByLabel('Colour (#rrggbb)')).toHaveValue('#2468dc')
  await expect(page.getByTestId('branding-preview').getByText('Alpha Hosting')).toBeVisible()
})

test('a reseller that empties every field sends nothing set, which puts the panel look back', async ({ page }) => {
  const script: ResellerBrandingScript = { saved: [] }
  await openBrandingPage(page, script)

  await page.getByRole('button', { name: 'Save branding' }).click()

  await expect(page.getByText('The branding has been saved.', { exact: false })).toBeVisible()
  expect(script.saved).toEqual([NO_BRANDING])
})

test('a value the panel refuses is shown in the panel sentence and the form keeps what was typed', async ({ page }) => {
  const refusal = 'This colour is too light: white text on it would be hard to read. Choose a darker colour.'
  await openBrandingPage(page, { saved: [], refusal })

  await page.getByLabel('Colour (#rrggbb)').fill('#ffcc00')
  await page.getByRole('button', { name: 'Save branding' }).click()

  await expect(page.getByText(refusal, { exact: true })).toBeVisible()
  await expect(page.getByLabel('Colour (#rrggbb)')).toHaveValue('#ffcc00')
  await expect(page.getByText('The branding has been saved.', { exact: false })).toHaveCount(0)
})

test("a customer's shell wears the branding of its reseller", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubShellBranding(page, { displayName: 'Alpha Hosting', accentColor: '#2468dc', logo: TINY_PNG })

  await page.goto('/')

  const header = page.getByRole('banner')
  await expect(header.getByText('Alpha Hosting')).toBeVisible()
  await expect(header.getByText('Maran', { exact: true })).toHaveCount(0)
  await expect(header.locator(`img[src="${TINY_PNG}"]`)).toBeVisible()
  const fill = await page.evaluate(() => {
    return document.documentElement.style.getPropertyValue('--acf')
  })
  expect(fill).toBe('#2468dc')
})

test("a customer whose reseller set nothing sees the panel's own name and colours", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, CUSTOMER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await stubMyAccount(page, OWN_ACCOUNT)
  const asked = await stubShellBranding(page, NO_BRANDING)

  await page.goto('/')

  await expect(page.getByRole('banner').getByText('Maran', { exact: true })).toBeVisible()
  expect(asked()).toBeGreaterThan(0)
  const fill = await page.evaluate(() => {
    return document.documentElement.style.getPropertyValue('--acf')
  })
  expect(fill).toBe('')
})

test('a reseller is not asked for the customer branding of its own shell', async ({ page }) => {
  await stubResellerSession(page, undefined, RESELLER_MODULES)
  const asked = await stubShellBranding(page, { displayName: 'Nope', accentColor: null, logo: null })

  await page.goto('/')
  await expect(page.getByRole('banner').getByText('Maran', { exact: true })).toBeVisible()

  expect(asked()).toBe(0)
})
