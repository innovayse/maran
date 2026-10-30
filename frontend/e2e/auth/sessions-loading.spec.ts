import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { Session } from '../../src/types/auth'

// The signed-in devices while they are on the way (#243): drawn from the table's own column schema.

/** The caller's sessions. */
const SESSIONS_PATTERN = '**/api/v1/sessions'

const CURRENT: Session = {
  id: '00000000-0000-0000-0000-0000000000a1',
  issuedAt: '2026-08-30T09:00:00+00:00',
  expiresAt: '2026-09-13T09:00:00+00:00',
  ipAddress: '203.0.113.7',
  userAgent: 'Chrome on Linux',
  isCurrent: true,
}

/**
 * Signs in.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

/** How the sessions screen is opened and answered. */
const SESSIONS_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await page.route(SESSIONS_PATTERN, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([CURRENT]) })
    })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await page.route(SESSIONS_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: 'The sessions could not be read.' }),
      })
    })
  },
  pattern: SESSIONS_PATTERN,
  path: '/settings/sessions',
  table: 'Devices signed in to your account',
  rowText: 'Chrome on Linux',
  failureText: 'The sessions could not be read.',
}

test('sessions: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, SESSIONS_SCENARIO)
})

test('sessions: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, SESSIONS_SCENARIO)
})

test('sessions: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, SESSIONS_SCENARIO)
})
