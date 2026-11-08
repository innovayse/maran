import { expect, test } from '@playwright/test'
import { CUSTOMER, EXAMPLE_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'

// One zone (issue #178): the overview, and the not-found
// state a zone another account owns gets. The records pane has its own spec.

test('the overview names the zone, its owner, its record count and its serial', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)

  await expect(page.getByRole('heading', { level: 1, name: 'example.com' })).toBeVisible()
  await expect(page.getByText('2026110701')).toBeVisible()
  await expect(page.getByText('alice', { exact: true })).toBeVisible()
  await expect(page.getByText('Last changed')).toBeVisible()
})

test('a zone that does not exist for the caller is the not-found state, not an alert', async ({ page }) => {
  await openDnsScreen(page, { zones: [] }, CUSTOMER)

  // Another account's zone and a zone that never existed look the same: the panel answers 404.
  await page.goto('/dns/zones/11111111-1111-1111-1111-111111111111')

  await expect(page.getByText('No such zone')).toBeVisible()
  await expect(page.getByText('This zone does not exist, or it belongs to another account.')).toBeVisible()
})

test('an address that cannot name a zone is not found without a request for it', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto('/dns/zones/not-a-zone')

  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible()
  expect(
    state.requests.some((request) => {
      return request.url.pathname.includes('not-a-zone')
    }),
  ).toBe(false)
})

test('a failed read shows the panel\'s reason', async ({ page }) => {
  await openDnsScreen(page, {
    zones: [EXAMPLE_ZONE],
    refusals: { 'GET dns-zones/:id': { detail: 'The DNS module is switched off on this host.', status: 503 } },
  })

  await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)

  await expect(page.getByText('The DNS module is switched off on this host.')).toBeVisible()
})

test('deleting the zone from its page asks first and leaves for the list', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByRole('dialog', { name: 'Delete the zone example.com?' }).getByRole('button', { name: 'Delete zone' }).click()

  await expect(page).toHaveURL(/\/dns\/zones$/)
  await expect(page.getByText('The zone example.com was deleted.')).toBeVisible()
})

test('the back link returns to the list', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto(`/dns/zones/${EXAMPLE_ZONE.id}`)
  await page.getByRole('link', { name: 'Back to the zones' }).click()

  await expect(page).toHaveURL(/\/dns\/zones$/)
})
