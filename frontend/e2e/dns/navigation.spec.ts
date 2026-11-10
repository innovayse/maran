import { expect, test } from '../fixtures/guarded-test'
import { breadcrumbItems, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { CUSTOMER, EXAMPLE_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'

// The DNS menu (issue #178, templates #452). The administrator is offered Zones, Templates and
// Nameservers; the customer is offered My zones, and a reseller is
// offered none of them, because the endpoints answer a reseller with no rows. The menu is advice;
// every endpoint refuses by role on its own. Navigation is by clicks only.

test('the administrator is offered Zones, Templates and Nameservers', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })
  await stubLandingReads(page)
  await page.goto('/')
  const nav = mainNavigation(page)

  await openSection(page, 'DNS')
  await expect(nav.getByRole('link', { name: 'Zones', exact: true })).toHaveAttribute('href', '/dns/zones')
  await expect(nav.getByRole('link', { name: 'Nameservers', exact: true })).toHaveAttribute('href', '/dns/nameservers')
  await expect(nav.getByRole('link', { name: 'Templates', exact: true })).toHaveAttribute('href', '/dns/templates')
  // None of the built entries is a disabled "coming soon" row.
  await expect(nav.getByRole('link', { name: 'Zones Coming soon' })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Templates Coming soon' })).toHaveCount(0)
})

test('the administrator reaches a zone from the list and the breadcrumb names the zone', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })
  await stubLandingReads(page)
  await page.goto('/')

  await openSection(page, 'DNS')
  await mainNavigation(page).getByRole('link', { name: 'Zones', exact: true }).click()
  await page.getByRole('link', { name: 'example.com', exact: true }).click()

  await expect(page).toHaveURL(`/dns/zones/${EXAMPLE_ZONE.id}`)
  await expect(breadcrumbItems(page).last()).toHaveText('example.com')
  await expect(breadcrumbItems(page).nth(1)).toHaveText('DNS')
})

test('the customer is offered My zones and neither the templates nor the nameservers', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] }, CUSTOMER)
  await stubLandingReads(page, 'customer')
  await page.goto('/')
  const nav = mainNavigation(page)

  // A section left with one page is that page's link under the section's own name (here DNS leads to
  // the customer's zones), not a submenu holding a single row.
  await expect(nav.getByRole('button', { name: 'Show the DNS pages' })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'DNS', exact: true })).toHaveAttribute('href', '/dns/zones')
  await expect(nav.getByRole('link', { name: 'Templates', exact: true })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Nameservers', exact: true })).toHaveCount(0)

  await nav.getByRole('link', { name: 'DNS', exact: true }).click()
  await expect(page).toHaveURL('/dns/zones')
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
})

test('a reseller is not offered the DNS section', async ({ page }) => {
  await stubResellerSession(page, undefined, [
    ...RESELLER_MODULES,
    { name: 'dns', displayName: 'DNS', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])
  await page.goto('/')
  const nav = mainNavigation(page)

  // Positive control: the reseller's own section is there, so the absence below is a finding.
  await expect(nav.getByRole('link', { name: 'Accounts' }).first()).toBeVisible()
  await expect(nav.getByRole('button', { name: 'Show the DNS pages' })).toHaveCount(0)
  await expect(nav.getByText('DNS', { exact: true })).toHaveCount(0)
})
