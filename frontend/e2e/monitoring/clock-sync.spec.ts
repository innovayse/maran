import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { STUBBED_HOST_METRICS, stubMonitoring } from '../fixtures/stub-monitoring-routes'
import type { ClockSync } from '../../src/types/monitoring'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true },
]

/** The sentence the panel sends for a clock whose sync service has not synchronised it. */
const NOT_SYNCHRONIZED_TEXT =
  'A time synchronisation service is enabled but has not synchronised the clock. Two-factor codes, ' +
  'sign-in sessions and certificates may fail until it does; check that the server can reach a time server.'

/**
 * Opens the monitoring screen with the clock read stubbed.
 * @param page The Playwright page under test.
 * @param clock The clock reading the panel answers.
 * @returns Resolves once the clock card has drawn.
 */
const openWithClock = async (page: Page, clock: ClockSync): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMonitoring(page, { lastDay: [], lastWeek: [] }, [], [], STUBBED_HOST_METRICS, clock)
  await page.goto('/monitoring')
  await expect(page.getByTestId('clock-sync-card')).toBeVisible({ timeout: 20_000 })
}

// The proposition: a clock the panel reports not synchronised is a WARNING on the screen carrying the
// panel's own sentence, with a badge naming the state. Breakable: a card that drew the sentence as a
// quiet line loses the status role; one that invented its own wording does not show the panel's text.
test('a clock that is not synchronised is a warning carrying the panel sentence', async ({ page }) => {
  await openWithClock(page, { state: 'notSynchronized', description: NOT_SYNCHRONIZED_TEXT })

  const card = page.getByTestId('clock-sync-card')
  await expect(card.getByText('Not synchronised', { exact: true })).toBeVisible()
  // Filtered by text: the card's loading region carries a status line of its own.
  await expect(card.getByRole('status').filter({ hasText: NOT_SYNCHRONIZED_TEXT })).toBeVisible()
})

// The inverse control: a synchronised clock draws no warning at all, only its explanation.
test('a synchronised clock draws no warning', async ({ page }) => {
  await openWithClock(page, {
    state: 'synchronized',
    description: 'The clock is synchronised with a time source.',
  })

  const card = page.getByTestId('clock-sync-card')
  await expect(card.getByText('Synchronised', { exact: true })).toBeVisible()
  await expect(card.getByText('The clock is synchronised with a time source.')).toBeVisible()
  await expect(card.getByRole('status').filter({ hasText: 'synchronised with a time source' })).toHaveCount(0)
})
