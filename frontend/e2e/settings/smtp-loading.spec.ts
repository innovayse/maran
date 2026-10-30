import type { Page } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { storedSmtpSettings, stubSmtpSettings } from '../fixtures/stub-smtp-routes'

// The mail settings form while the settings are on the way (#243): real labels over placeholder controls.

const SMTP_PATTERN = '**/api/v1/notifications/smtp'

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The mail settings could not be read.'

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

describeRegionLoading('mail settings', {
  stub: async (page) => {
    await shell(page)
    await stubSmtpSettings(page, storedSmtpSettings)
  },
  stubFailure: async (page) => {
    await shell(page)
    await page.route(SMTP_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: SMTP_PATTERN,
  path: '/settings/smtp',
  anchor: (page) => {
    return page.getByText('Mail server', { exact: true })
  },
  // Whether a password is stored is part of what loads; its hint replaces a placeholder line.
  contentText: 'A password is saved. Type one here only to replace it.',
  failureText: REFUSAL,
})
