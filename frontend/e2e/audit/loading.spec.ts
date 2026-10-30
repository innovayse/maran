import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { AuditEvent } from '../../src/types/audit'

// The audit journal while its entries are on the way (#243): drawn from its own column schema.

/** The journal's read, with or without a query. */
const AUDIT_PATTERN = '**/api/v1/audit*'

const SIGNED_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-09T09:08:00+00:00',
  actorUsername: 'verifier',
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'verifier',
  ipAddress: '127.0.0.1',
  succeeded: true,
}

/**
 * Signs in as an administrator.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

describeTableLoading('audit journal', {
  stub: async (page) => {
    await signIn(page)
    await page.route(AUDIT_PATTERN, async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([SIGNED_IN]) })
    })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await page.route(AUDIT_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: 'The journal could not be read.' }),
      })
    })
  },
  pattern: AUDIT_PATTERN,
  path: '/settings/audit',
  table: 'Recent audit entries',
  rowText: 'LoginSucceeded',
  failureText: 'The journal could not be read.',
})
