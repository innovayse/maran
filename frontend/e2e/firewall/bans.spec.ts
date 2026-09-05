import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import type { Ban } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
]

/** The instant the fake clock is installed at: shortly before the one every assertion is written against. */
const BEFORE_TEST_TIME = new Date('2026-09-01T11:59:50.000Z')

/** The instant the clock is paused at, which is where the countdown assertions start. */
const TEST_TIME = new Date('2026-09-01T12:00:00.000Z')

// Thirty minutes past the paused instant, so the first countdown reads a round number and the
// arithmetic a reader has to do to check this spec is arithmetic they can do in their head.
const BRUTE_FORCE: Ban = {
  id: '00000000-0000-0000-0000-0000000000b1',
  ipAddress: '198.51.100.4',
  reason: 'bruteForce',
  failures: 12,
  bannedAt: '2026-09-01T11:45:00+00:00',
  expiresAt: '2026-09-01T12:30:00+00:00',
}

// The other half of the reason column, and the row with no countdown to tick.
const MANUAL: Ban = {
  id: '00000000-0000-0000-0000-0000000000b2',
  ipAddress: '203.0.113.9',
  reason: 'manual',
  failures: 0,
  bannedAt: '2026-09-01T09:00:00+00:00',
  expiresAt: null,
}

/**
 * Puts the firewall screen in front of an administrator with the given bans in force.
 * @param page The Playwright page under test.
 * @param bans The bans the stubbed panel reports.
 * @returns Resolves once the screen has been navigated to.
 */
const openScreen = async (page: Page, bans: Ban[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [], bans, whitelist: [] })
  await page.goto('/firewall')
}

// The expiry is the one value on this screen that changes while nobody touches the page, and it is
// driven by a fake clock rather than by a sleep: `formatCountdown` takes the instant to measure
// from as a parameter for exactly this reason, and a test that waited for the wall clock would be
// the flaky one rules/testing.md forbids.
test('a ban expiry counts down as time passes, and says so once it has run out', async ({ page }) => {
  // Installed BEFORE the navigation and set slightly early, so every timer the page needs while it
  // loads still runs; the pause below is what freezes the moment the assertions are written against.
  await page.clock.install({ time: BEFORE_TEST_TIME })
  await openScreen(page, [BRUTE_FORCE])
  await page.clock.pauseAt(TEST_TIME)

  const row = page.getByRole('row').filter({ hasText: BRUTE_FORCE.ipAddress })
  await expect(row).toContainText('in 30 minutes')

  await page.clock.fastForward('20:00')
  await expect(row).toContainText('in 10 minutes')

  // Past the expiry the suffix has to flip: a distance with no direction reads as time remaining,
  // and an operator would leave a lifted address banned on the strength of it.
  await page.clock.fastForward('15:00')
  await expect(row).toContainText('5 minutes ago')
})

// A ban with no expiry is not a countdown of zero: the contract spells "until somebody lifts it" as
// an absent duration, and the column has to say so in words rather than render an empty cell.
test('a ban with no expiry reads as one that lasts until somebody lifts it', async ({ page }) => {
  await page.clock.install({ time: BEFORE_TEST_TIME })
  await openScreen(page, [MANUAL])
  await page.clock.pauseAt(TEST_TIME)

  const row = page.getByRole('row').filter({ hasText: MANUAL.ipAddress })
  await expect(row).toContainText('Until it is lifted')
})

// The reason column is why this table reads the panel's rows rather than the kernel's ban set: the
// agent stores no reason and cannot, so these two words are the product's whole answer to "why is
// this address cut off".
test('the bans table names why each address is refused, and how many failures were counted', async ({
  page,
}) => {
  await openScreen(page, [BRUTE_FORCE, MANUAL])

  const detected = page.getByRole('row').filter({ hasText: BRUTE_FORCE.ipAddress })
  await expect(detected).toContainText('Brute force')
  await expect(detected).toContainText('12')

  const byHand = page.getByRole('row').filter({ hasText: MANUAL.ipAddress })
  await expect(byHand).toContainText('By hand')
})

test('an empty ban list says nobody is banned instead of rendering an empty table', async ({
  page,
}) => {
  await openScreen(page, [])

  await expect(page.getByText('Nobody is banned')).toBeVisible()
  await expect(page.getByRole('table', { name: 'Banned addresses' })).toHaveCount(0)
})

// Lifting a ban lets an address back in, so it is confirmed first — and the request must carry the
// ADDRESS, which is what the module's `DELETE /api/v1/firewall/bans` binds from. A lift that sent
// the episode id would answer 200 and lift nothing.
test('lifting a ban asks first, and then names the address in the request', async ({ page }) => {
  const deletes: string[] = []
  await openScreen(page, [BRUTE_FORCE])
  page.on('request', (request) => {
    if (request.method() === 'DELETE' && request.url().includes('/api/v1/firewall/bans')) {
      deletes.push(request.url())
    }
  })

  const row = page.getByRole('row').filter({ hasText: BRUTE_FORCE.ipAddress })
  await row.getByRole('button', { name: `Actions for ${BRUTE_FORCE.ipAddress}` }).click()
  await page.getByRole('menuitem', { name: 'Lift the ban' }).click()

  await expect(row).toContainText('Let this address back in?')
  expect(deletes).toEqual([])

  await row.getByRole('button', { name: 'Yes, lift it' }).click()

  await expect.poll(() => {
    return deletes
  }).toHaveLength(1)
  expect(deletes[0]).toContain(`address=${encodeURIComponent(BRUTE_FORCE.ipAddress)}`)
})
