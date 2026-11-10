import { expect, test } from '../fixtures/guarded-test'
import { FULL_CATALOGUE, mainNavigation, openSection } from '../fixtures/open-full-shell'
import { storedAlertRecipients, stubAlertRecipientsRefused, stubAlertSettings } from '../fixtures/stub-alert-routes'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Page } from '@playwright/test'

// The alert settings screen (administrators only): a recipient list edited as a draft and saved
// whole, and the thresholds with the bounds the panel states. Every spec reaches the page through
// the sidebar's Settings group.

/** The address the 390px check below opens; `e2e/ui-kit/route-coverage.spec.ts` reads it from here. */
const SWEPT_SCREEN = { path: '/settings/alerts' }

/**
 * Opens the alert settings page by the menu, as a person would.
 * @param page The Playwright page under test.
 * @returns Resolves once the page's heading is on screen.
 */
const openAlertSettings = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto('/')
  await openSection(page, 'Settings')
  await mainNavigation(page).getByRole('link', { name: 'Alert settings', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Alert settings' })).toBeVisible()
}

test('the recipient list shows what is stored and a new address is saved with the whole list', async ({ page }) => {
  const calls = await stubAlertSettings(page)
  await openAlertSettings(page)

  const list = page.getByRole('list', { name: 'Alert recipients' })
  await expect(list.getByText('ops@example.net')).toBeVisible()
  await expect(list.getByText('oncall@example.net')).toBeVisible()

  await page.getByLabel('Address to add').fill('night@example.net')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByRole('button', { name: 'Remove ops@example.net' }).click()
  await page.getByRole('button', { name: 'Save recipients' }).click()

  await expect(page.getByText('The alert recipients have been saved.')).toBeVisible()
  expect(calls.recipientSaves).toEqual([{ addresses: ['oncall@example.net', 'night@example.net'] }])
})

test('an invalid or repeated address is refused before it joins the list', async ({ page }) => {
  await stubAlertSettings(page)
  await openAlertSettings(page)

  await page.getByLabel('Address to add').fill('not-an-address')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await expect(page.getByText('Enter a valid email address.')).toBeVisible()

  // The inverse control: a well-formed address that is already listed is refused for a different reason.
  await page.getByLabel('Address to add').fill('OPS@example.net')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await expect(page.getByText('That address is already in the list.')).toBeVisible()
})

test('a save the panel refuses shows its sentence verbatim', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/notifications\/alert-recipients$/)
  await stubAlertSettings(page)
  await stubAlertRecipientsRefused(page, 'The address ops@example.net is not accepted by the mail policy.')
  await openAlertSettings(page)

  await page.getByRole('button', { name: 'Save recipients' }).click()
  await expect(page.getByText('The address ops@example.net is not accepted by the mail policy.')).toBeVisible()
})

test('an empty recipient list is saved as an empty list', async ({ page }) => {
  const calls = await stubAlertSettings(page, { ...storedAlertRecipients, addresses: [] })
  await openAlertSettings(page)

  await expect(page.getByText('No recipients: alert mail is not sent.')).toBeVisible()
  await page.getByRole('button', { name: 'Save recipients' }).click()
  await expect(page.getByText('The alert recipients have been saved.')).toBeVisible()
  expect(calls.recipientSaves).toEqual([{ addresses: [] }])
})

test('the thresholds are saved with the processor and memory watched only when ticked', async ({ page }) => {
  const calls = await stubAlertSettings(page)
  await openAlertSettings(page)

  await expect(page.getByLabel('Disk usage threshold (%)')).toHaveValue('90')
  // Not watched: no field for it is offered.
  await expect(page.getByLabel('Processor usage threshold (%)')).toHaveCount(0)

  await page.getByLabel('Watch processor usage').check({ force: true })
  await page.getByLabel('Processor usage threshold (%)').fill('85')
  await page.getByLabel('Disk usage threshold (%)').fill('80')
  await page.getByRole('button', { name: 'Save thresholds' }).click()

  await expect(page.getByText('The alert thresholds have been saved.')).toBeVisible()
  expect(calls.thresholdSaves).toEqual([{ diskPercent: 80, cpuPercent: 85, memoryPercent: null }])

  // Switching it off again sends null: the panel stops watching it.
  await page.getByLabel('Watch processor usage').uncheck({ force: true })
  await page.getByRole('button', { name: 'Save thresholds' }).click()
  await expect.poll(() => {return calls.thresholdSaves.length}).toBe(2)
  expect(calls.thresholdSaves[1]).toEqual({ diskPercent: 80, cpuPercent: null, memoryPercent: null })
})

test('a threshold outside the panel’s bounds is refused before anything is sent', async ({ page }) => {
  const calls = await stubAlertSettings(page)
  await openAlertSettings(page)

  await page.getByLabel('Disk usage threshold (%)').fill('100')
  await page.getByRole('button', { name: 'Save thresholds' }).click()

  // The bounds in the sentence are the panel's own (50 to 99), not a constant of the screen.
  await expect(page.getByText('Enter a whole number from 50 to 99.')).toBeVisible()
  expect(calls.thresholdSaves).toEqual([])

  // The inverse control: the top of the range is accepted.
  await page.getByLabel('Disk usage threshold (%)').fill('99')
  await page.getByRole('button', { name: 'Save thresholds' }).click()
  await expect.poll(() => {return calls.thresholdSaves.length}).toBe(1)
})

test('the page fits a 390px screen in Armenian', async ({ page }) => {
  await stubAlertSettings(page)
  await setPersistedLocale(page, 'hy')
  await page.setViewportSize({ width: 390, height: 844 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, FULL_CATALOGUE)
  await page.goto(SWEPT_SCREEN.path)

  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  const overflow = await page.evaluate(() => {
    return document.documentElement.scrollWidth - document.documentElement.clientWidth
  })
  expect(overflow).toBeLessThanOrEqual(0)
})
