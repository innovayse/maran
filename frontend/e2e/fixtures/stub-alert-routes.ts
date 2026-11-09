import type { Page } from '@playwright/test'
import type {
  AlertRecipients,
  AlertThresholds,
  SaveAlertRecipientsRequest,
  SaveAlertThresholdsRequest,
} from '../../src/types/alertSettings'

/** The recipients a configured panel reports. */
export const storedAlertRecipients: AlertRecipients = { addresses: ['ops@example.net', 'oncall@example.net'], maximum: 10 }

/** The thresholds of a panel that has never been configured: disk at 90, the others not watched. */
export const defaultAlertThresholds: AlertThresholds = {
  diskPercent: 90,
  cpuPercent: null,
  memoryPercent: null,
  minimumPercent: 50,
  maximumPercent: 99,
}

/** What the stubbed alert endpoints saw, so a spec can assert on the exact bodies sent. */
export interface AlertCalls {
  /** The body of every `PUT .../alert-recipients`, in order. */
  recipientSaves: SaveAlertRecipientsRequest[]
  /** The body of every `PUT .../alert-thresholds`, in order. */
  thresholdSaves: SaveAlertThresholdsRequest[]
}

/**
 * Fulfils both alert-settings endpoints: GET answers the current value, PUT stores the body, answers
 * 204 and makes the next GET return it, as the real panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param recipients The recipients the panel starts with.
 * @param thresholds The thresholds the panel starts with.
 * @returns The record of what was saved.
 */
export const stubAlertSettings = async (
  page: Page,
  recipients: AlertRecipients = storedAlertRecipients,
  thresholds: AlertThresholds = defaultAlertThresholds,
): Promise<AlertCalls> => {
  const calls: AlertCalls = { recipientSaves: [], thresholdSaves: [] }
  let currentRecipients = recipients
  let currentThresholds = thresholds

  await page.route('**/api/v1/notifications/alert-recipients', async (route) => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON() as SaveAlertRecipientsRequest
      calls.recipientSaves.push(body)
      currentRecipients = { ...currentRecipients, ...body }
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(currentRecipients) })
  })
  await page.route('**/api/v1/monitoring/alert-thresholds', async (route) => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON() as SaveAlertThresholdsRequest
      calls.thresholdSaves.push(body)
      currentThresholds = { ...currentThresholds, ...body }
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(currentThresholds) })
  })

  return calls
}

/**
 * Fulfils `PUT /api/v1/notifications/alert-recipients` with a refusal carrying the panel's own
 * sentence in the RFC 7807 `detail`; the read stays as {@link stubAlertSettings} installed it.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The refusal text the panel sends.
 * @returns Resolves once the route is installed.
 */
export const stubAlertRecipientsRefused = async (page: Page, detail: string): Promise<void> => {
  await page.route('**/api/v1/notifications/alert-recipients', async (route) => {
    if (route.request().method() === 'PUT') {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'AlertRecipientInvalid', detail }),
      })
      return
    }
    await route.fallback()
  })
}
