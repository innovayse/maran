import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { test } from '../fixtures/guarded-test'
import { AUDIT_PATTERN, stubAudit, stubAuditProblem } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { AuditEvent } from '../../src/types/audit'
import type { Page } from '@playwright/test'

// The audit journal while its entries are on the way (#243): drawn from its own column schema.

const SIGNED_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-09T09:08:00+00:00',
  actorUsername: 'verifier',
  impersonatorUsername: null,
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'verifier',
  subjectName: 'verifier',
  ipAddress: '127.0.0.1',
  succeeded: true,
  outcomeName: 'Succeeded',
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
  await stubShellReads(page)
}

/** How the audit journal screen is opened and answered. */
const AUDIT_JOURNAL_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubAudit(page, [SIGNED_IN])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubAuditProblem(page, 500, 'The journal could not be read.')
  },
  pattern: AUDIT_PATTERN,
  path: '/settings/audit',
  table: 'Recent audit entries',
  rowText: 'LoginSucceeded',
  failureText: 'The journal could not be read.',
}

test('audit journal: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, AUDIT_JOURNAL_SCENARIO)
})

test('audit journal: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, AUDIT_JOURNAL_SCENARIO)
})

test('audit journal: a slow failure is an alert, never a skeleton', async ({ page, guard }) => {
  // The journal's read failing is the subject.
  guard.allow(500, AUDIT_PATTERN)
  await expectTableSlowFailure(page, AUDIT_JOURNAL_SCENARIO)
})
