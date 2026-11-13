import { expect, test } from '../fixtures/guarded-test'
import { openMarketplace } from '../fixtures/open-marketplace'
import {
  catalogueModule,
  enabledWorld,
  installedModule,
  type MarketplaceWorld,
} from '../fixtures/stub-marketplace-routes'
import type { MarketplaceModule } from '../../src/types/marketplace'

// Issue #187: what the Available tab draws for each state of the catalogue, every sentence the
// server's own. A negative claim ("no install button", "no warning") is paired with a card in the
// same catalogue that must show it, so a page that had stopped drawing buttons or alerts at all
// would fail the control instead of passing the negative.

/** An entitled free module that can be installed: the control for every refusal below. */
const WEATHER = catalogueModule('weather', 'Weather widgets')

/** A second installable module, so a search has something to leave out. */
const BACKUPS = catalogueModule('backup-viewer', 'Backup viewer', { summary: 'Browse backup archives.' })

/** A paid module whose licence is missing, with the cabinet link the server marked. */
const PAID = catalogueModule('paid-reports', 'Paid reports', {
  tier: 'business',
  tierDisplayName: 'Business',
  price: { display: '9 EUR a month', purchaseUrl: 'https://cabinet.example.com/buy/paid-reports' },
  entitlement: { state: 'notPurchased', stateDisplayName: 'Licence not purchased', until: null },
  canInstall: false,
  cannotInstallMessage: 'A licence is required to install this module.',
})

/** A paid module the licence covers. */
const LICENSED = catalogueModule('licensed-analytics', 'Licensed analytics', {
  tier: 'business',
  tierDisplayName: 'Business',
  price: { display: '12 EUR a month', purchaseUrl: 'https://cabinet.example.com/buy/licensed-analytics' },
  entitlement: { state: 'entitled', stateDisplayName: 'Licensed', until: '2027-01-01T00:00:00Z' },
})

/**
 * A world with the marketplace on and the given catalogue entries.
 * @param items The entries the catalogue lists.
 * @param overrides Further fields of the world to change.
 * @returns The world.
 */
const worldWith = (items: MarketplaceModule[], overrides: Partial<MarketplaceWorld> = {}): MarketplaceWorld => {
  const world = enabledWorld(overrides)
  world.catalogue = { ...world.catalogue, items }
  return world
}

test('the catalogue lists every module the server offers and says when it was fetched', async ({ page }) => {
  await openMarketplace(page, worldWith([WEATHER, BACKUPS]))

  const list = page.getByTestId('catalogue-list')
  await expect(list.getByTestId('module-card-weather')).toBeVisible()
  await expect(list.getByTestId('module-card-backup-viewer')).toBeVisible()
  await expect(page.getByText(/^Catalogue fetched /)).toBeVisible()
  await expect(page.getByTestId('catalogue-unverified')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Install Weather widgets' })).toBeEnabled()
})

test('a card prints the publisher display name and never the publisher id', async ({ page }) => {
  // The positive checks on the display names are the control: a page that had stopped drawing the publisher
  // row would fail them, so the absence of the id cannot pass by an empty row.
  const named = catalogueModule('weather', 'Weather widgets', { publisher: 'Innovayse Ltd', publisherId: 'innovayse-ltd' })
  const other = catalogueModule('backup-viewer', 'Backup viewer', { publisher: 'Acme Works', publisherId: 'acme' })
  await openMarketplace(page, worldWith([named, other]))

  const card = page.getByTestId('module-card-weather')
  await expect(card.getByText('Innovayse Ltd')).toBeVisible()
  await expect(card.getByText('innovayse-ltd')).toHaveCount(0)
  await expect(page.getByTestId('module-card-backup-viewer').getByText('Acme Works')).toBeVisible()
})

test('searching asks the server and clearing the search brings every module back', async ({ page }) => {
  const recorder = await openMarketplace(page, worldWith([WEATHER, BACKUPS]))
  await expect(page.getByTestId('module-card-backup-viewer')).toBeVisible()

  const search = page.getByRole('searchbox', { name: 'Search modules' })
  await search.fill('weather')

  await expect(page.getByTestId('module-card-backup-viewer')).toHaveCount(0)
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
  expect(recorder.matching(/^GET \/api\/v1\/marketplace\/catalogue\?search=weather$/)).not.toEqual([])

  await search.fill('zzz-nothing')
  await expect(page.getByText('No modules match', { exact: true })).toBeVisible()
  await expect(page.getByText('No modules available', { exact: true })).toHaveCount(0)

  await search.fill('')
  await expect(page.getByTestId('module-card-backup-viewer')).toBeVisible()
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
})

test('an empty catalogue says so, and a catalogue with modules does not', async ({ page }) => {
  await openMarketplace(page, worldWith([]))

  await expect(page.getByText('No modules available', { exact: true })).toBeVisible()
  await expect(page.getByTestId('catalogue-list')).toHaveCount(0)
})

test('a module without a licence has no install button, shows why and links to the cabinet', async ({ page }) => {
  await openMarketplace(page, worldWith([PAID, LICENSED]))

  const paid = page.getByTestId('module-card-paid-reports')
  await expect(paid).toBeVisible()
  await expect(paid.getByTestId('module-cannot-install')).toHaveText('A licence is required to install this module.')
  await expect(paid.getByTestId('module-licence-badge')).toHaveText('Licence not purchased')
  await expect(paid.getByTestId('module-buy-link')).toHaveAttribute(
    'href',
    'https://cabinet.example.com/buy/paid-reports',
  )
  await expect(paid.getByRole('button', { name: /^Install / })).toHaveCount(0)

  // The positive control: the licensed module in the same catalogue is offered, with no refusal.
  const licensed = page.getByTestId('module-card-licensed-analytics')
  await expect(licensed.getByRole('button', { name: 'Install Licensed analytics' })).toBeEnabled()
  await expect(licensed.getByTestId('module-cannot-install')).toHaveCount(0)
  await expect(licensed.getByTestId('module-buy-link')).toHaveCount(0)
})

test('a module built for another panel version is shown with its reason and cannot be installed', async ({ page }) => {
  const legacy = catalogueModule('legacy-tools', 'Legacy tools', {
    state: 'incompatible',
    canInstall: false,
    cannotInstallMessage: 'Needs panel version 2.0.0 or newer.',
  })
  await openMarketplace(page, worldWith([legacy, WEATHER]))

  const card = page.getByTestId('module-card-legacy-tools')
  await expect(card.getByText('Not compatible', { exact: true })).toBeVisible()
  await expect(card.getByTestId('module-cannot-install')).toHaveText('Needs panel version 2.0.0 or newer.')
  await expect(card.getByRole('button', { name: /^Install / })).toHaveCount(0)

  await expect(page.getByTestId('module-card-weather').getByRole('button', { name: 'Install Weather widgets' })).toBeEnabled()
  await expect(page.getByTestId('module-card-weather').getByTestId('module-cannot-install')).toHaveCount(0)
})

test('an unverified catalogue disables installing and prints the server’s message', async ({ page }) => {
  const world = worldWith([WEATHER])
  world.catalogue = {
    ...world.catalogue,
    verified: false,
    unverifiedMessage: 'The catalogue signature could not be checked.',
  }
  await openMarketplace(page, world)

  const notice = page.getByTestId('catalogue-unverified')
  await expect(notice).toContainText('The catalogue could not be verified')
  await expect(notice).toContainText('The catalogue signature could not be checked.')
  await expect(notice).toContainText('Installed modules are not affected.')
  await expect(page.getByRole('button', { name: 'Install Weather widgets' })).toBeDisabled()
  // The list itself is still drawn: only acting on it is withheld.
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
})

test('an unreachable marketplace says so and leaves the installed list readable', async ({ page, guard }) => {
  guard.allow(503, /\/api\/v1\/marketplace\/catalogue/)
  const world = worldWith([WEATHER], {
    installed: [installedModule('clock', 'Clock')],
    catalogueRefusal: { status: 503, code: 'MarketplaceUnreachable', detail: 'The marketplace did not answer.' },
  })
  await openMarketplace(page, world)

  const notice = page.getByTestId('marketplace-unreachable')
  await expect(notice).toContainText('The marketplace could not be reached')
  await expect(notice).toContainText('The marketplace did not answer.')
  await expect(page.getByText('No modules available', { exact: true })).toHaveCount(0)

  await page.getByRole('tab', { name: 'Installed' }).click()
  await expect(page.getByRole('cell', { name: /^Clock/ })).toBeVisible()

  // The inverse control: once the marketplace answers, the same tab draws its modules and no notice.
  world.catalogueRefusal = null
  await page.getByRole('tab', { name: 'Available' }).click()
  await expect(page.getByTestId('module-card-weather')).toBeVisible()
  await expect(page.getByTestId('marketplace-unreachable')).toHaveCount(0)
})

test('a withdrawn module shows the warning and the reason, and a healthy one shows neither', async ({ page }) => {
  const withdrawn = catalogueModule('weather-old', 'Old weather', {
    state: 'revoked',
    canInstall: false,
    cannotInstallMessage: 'Withdrawn by the publisher: a security fix is required.',
  })
  await openMarketplace(
    page,
    worldWith([withdrawn, WEATHER], {
      installed: [installedModule('clock', 'Clock', { revoked: true })],
    }),
  )

  const card = page.getByTestId('module-card-weather-old')
  await expect(card.getByTestId('module-revoked')).toContainText('Withdrawn')
  await expect(card.getByTestId('module-revoked')).toContainText('a security fix is required')
  await expect(card.getByRole('button', { name: /^Install / })).toHaveCount(0)
  await expect(page.getByTestId('module-card-weather').getByTestId('module-revoked')).toHaveCount(0)

  // An installed module that was withdrawn keeps its removal.
  await page.getByRole('tab', { name: 'Installed' }).click()
  await expect(page.getByText('This version was withdrawn by the marketplace.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Remove Clock' })).toBeEnabled()
})
