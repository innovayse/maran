import { expect, test } from '@playwright/test'
import { breadcrumbItems, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { CUSTOMER, EXAMPLE_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'
import { RESELLER_MODULES, stubResellerSession } from '../fixtures/stub-reseller-session'

// The DNS menu (issue #178). The administrator is offered Zones and Nameservers, and Templates as a
// "coming soon" row until its endpoints exist; the customer is offered My zones, and a reseller is
// offered none of them, because the endpoints answer a reseller with no rows. The menu is advice;
// every endpoint refuses by role on its own. Navigation is by clicks only.

test('the administrator is offered Zones and Nameservers, and Templates only as a coming-soon row', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })
  await page.goto('/')
  const nav = mainNavigation(page)

  await openSection(page, 'DNS')
  await expect(nav.getByRole('link', { name: 'Zones', exact: true })).toHaveAttribute('href', '/dns/zones')
  await expect(nav.getByRole('link', { name: 'Nameservers', exact: true })).toHaveAttribute('href', '/dns/nameservers')
  // Neither built entry is the disabled "coming soon" row it used to be.
  await expect(nav.getByRole('link', { name: 'Zones Coming soon' })).toHaveCount(0)
  // Templates has no page yet, so it is the disabled row: its endpoints are a later phase of the issue.
  await expect(nav.getByRole('link', { name: 'Templates', exact: true })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Templates Coming soon' })).toHaveAttribute('aria-disabled', 'true')
})

test('the administrator reaches a zone from the list and the breadcrumb names the zone', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })
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
