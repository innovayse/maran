import { expect, test } from '@playwright/test'
import { ALICE, BOB, CUSTOMER, EXAMPLE_ZONE, SHOP_ZONE, openDnsScreen } from '../fixtures/open-dns-screen'

// The DNS zones list (issue #178) is server-paged like every list: the page, its size, the search,
// the sort and the filters live in the URL, the panel answers one page with each row's owner, and
// the SPA filters nothing. One page serves the administrator's "Zones" and the customer's "My
// zones".

test('the zones list shows each zone with its owner, record count and serial', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

  await page.goto('/dns/zones')

  await expect(page.getByRole('heading', { level: 1, name: 'DNS zones' })).toBeVisible()
  const rows = page.getByRole('row')
  await expect(rows.filter({ hasText: 'example.com' })).toContainText('alice')
  await expect(rows.filter({ hasText: 'example.com' })).toContainText('2026110701')
  await expect(rows.filter({ hasText: 'shop.example.org' })).toContainText('bob')
  await expect(rows.filter({ hasText: 'shop.example.org' })).toContainText('2026110502')
})

test('the list shows the empty state when the panel reports no zones', async ({ page }) => {
  await openDnsScreen(page, { zones: [] })

  await page.goto('/dns/zones')

  await expect(page.getByText('No zones yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search zones' })).toHaveCount(0)
})

test('a search is sent to the panel, which answers the matching zones', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

  await page.goto('/dns/zones')
  await page.getByRole('searchbox', { name: 'Search zones' }).fill('shop')

  await expect(page).toHaveURL(/search=shop/)
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)
  expect(
    state.requests.some((request) => {
      return request.url.pathname.endsWith('/dns-zones') && request.url.searchParams.get('search') === 'shop'
    }),
  ).toBe(true)
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto('/dns/zones?search=nowhere')

  await expect(page.getByText('No zones match')).toBeVisible()
  await expect(page.getByText('No zones yet')).toHaveCount(0)

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/dns\/zones$/)
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
})

test('the account chosen in the filter sheet becomes a removable chip and narrows the server page', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

  await page.goto('/dns/zones')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'bob' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)
  expect(
    state.requests.some((request) => {
      return request.url.searchParams.get('accountId') === BOB.id
    }),
  ).toBe(true)

  await page.getByRole('button', { name: 'Remove filter: Account: bob' }).click()
  await expect(page).not.toHaveURL(/account=/)
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
})

test('the account filter narrows to one owner', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

  await page.goto(`/dns/zones?account=${BOB.id}`)

  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)
})

test('the panel refusing the list is shown in its own words, with no table beside it', async ({ page }) => {
  await openDnsScreen(page, {
    zones: [EXAMPLE_ZONE],
    refusals: { 'GET dns-zones': { detail: 'The DNS module is switched off on this host.', status: 503 } },
  })

  await page.goto('/dns/zones')

  await expect(page.getByText('The DNS module is switched off on this host.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('deleting a zone asks first, says what it does, and reads the list again', async ({ page }) => {
  const state = await openDnsScreen(page, { zones: [EXAMPLE_ZONE, SHOP_ZONE] })

  await page.goto('/dns/zones')
  await page.getByRole('button', { name: 'Actions for the zone example.com' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()

  const dialog = page.getByRole('dialog', { name: 'Delete the zone example.com?' })
  await expect(dialog).toContainText('Every record in the zone is deleted')
  // Nothing was sent by merely asking.
  expect(
    state.requests.some((request) => {
      return request.method === 'DELETE'
    }),
  ).toBe(false)

  await dialog.getByRole('button', { name: 'Delete zone' }).click()

  await expect(page.getByText('The zone example.com was deleted.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'shop.example.org' })).toBeVisible()
})

test('a refused delete keeps the zone and shows the panel\'s reason', async ({ page }) => {
  await openDnsScreen(page, {
    zones: [EXAMPLE_ZONE],
    refusals: { 'DELETE dns-zones/:id': { detail: 'The zone is being applied. Try again in a moment.' } },
  })

  await page.goto('/dns/zones')
  await page.getByRole('button', { name: 'Actions for the zone example.com' }).click()
  await page.getByRole('menuitem', { name: 'Delete' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete zone' }).click()

  await expect(page.getByText('The zone is being applied. Try again in a moment.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
})

test('a customer sees their own zones without the owner column or any filter', async ({ page }) => {
  await openDnsScreen(page, { zones: [{ ...EXAMPLE_ZONE, accountId: ALICE.id }] }, CUSTOMER)

  await page.goto('/dns/zones')

  await expect(page.getByRole('link', { name: 'example.com', exact: true })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Owner' })).toHaveCount(0)

  // The account is the list's one filter and a customer's list is already their own account's.
  await expect(page.getByTestId('filter-trigger')).toHaveCount(0)
  await expect(page.getByRole('searchbox', { name: 'Search zones' })).toBeVisible()
})

test('an administrator sees the owner column, the positive control for the customer view above', async ({ page }) => {
  await openDnsScreen(page, { zones: [EXAMPLE_ZONE] })

  await page.goto('/dns/zones')

  await expect(page.getByRole('columnheader', { name: 'Owner' })).toBeVisible()
})
