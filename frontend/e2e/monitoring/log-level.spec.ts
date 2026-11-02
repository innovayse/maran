import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { logLevelChanges, stubbedBucket, stubMonitoring } from '../fixtures/stub-monitoring-routes'
import type { PanelModule } from '../../src/types/module'

// The panel's runtime log level on the monitoring screen (issue #131): the card shows what the
// panel's logger is writing at, and applying another level asks the panel and reads the answer back.

const LICENSED: PanelModule[] = [{ name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true }]

/**
 * Opens the monitoring screen as a signed-in administrator with every read stubbed.
 * @param page The Playwright page under test.
 * @returns Resolves once the log level card has its answer.
 */
const openMonitoring = async (page: Page): Promise<void> => {
  logLevelChanges.length = 0
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMonitoring(
    page,
    { lastDay: [stubbedBucket(0, 11.5)], lastWeek: [stubbedBucket(0, 91.5)] },
    [{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }],
    [],
  )
  await page.goto('/monitoring')
  await expect(page.getByTestId('log-level-card').getByRole('combobox', { name: 'Log level' })).toHaveText(
    'Information — the normal level',
    { timeout: 20_000 },
  )
}

// The proposition: the card names the level the panel reported, and offers nothing to apply until a
// different level is picked. Breakable: a card that ignored the panel's level shows a placeholder;
// one that left the button live would let an administrator journal a change that changes nothing.
test('the log level card shows the running level and offers nothing to apply until another is picked', async ({ page }) => {
  await openMonitoring(page)

  await expect(page.getByTestId('log-level-card').getByRole('button', { name: 'Apply level' })).toBeDisabled()
})

// The proposition: picking a level and applying it sends exactly that level, confirms it, and shows
// the level the panel reads back. Breakable: a card that sent the old level, or none, fails the
// recorded request; one that showed its own pick instead of the read-back would still pass the text,
// which is why the stub answers the read with what it was sent and nothing else.
test('applying a level sends it, confirms it and shows what the panel reads back', async ({ page }) => {
  await openMonitoring(page)
  const card = page.getByTestId('log-level-card')

  await card.getByRole('combobox', { name: 'Log level' }).click()
  await page.getByRole('option', { name: 'Warning — only what needs attention' }).click()
  await card.getByRole('button', { name: 'Apply level' }).click()

  await expect(card.getByText('The log level has been changed.')).toBeVisible()
  expect(logLevelChanges).toEqual(['warning'])
  await expect(card.getByRole('combobox', { name: 'Log level' })).toHaveText('Warning — only what needs attention')
  await expect(card.getByRole('button', { name: 'Apply level' })).toBeDisabled()
})

// The proposition: a refused change shows the panel's own sentence, verbatim, inside the card.
test('a refused change shows the panel’s own message in the card', async ({ page }) => {
  await openMonitoring(page)
  await page.route(/\/api\/v1\/log-level$/, async (route) => {
    if (route.request().method() !== 'PUT') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'LogLevelUnknown', title: 'That is not a log level the panel offers.' }),
    })
  })
  const card = page.getByTestId('log-level-card')

  await card.getByRole('combobox', { name: 'Log level' }).click()
  await page.getByRole('option', { name: 'Debug — diagnostic detail' }).click()
  await card.getByRole('button', { name: 'Apply level' }).click()

  await expect(card.getByText('That is not a log level the panel offers.')).toBeVisible()
})
