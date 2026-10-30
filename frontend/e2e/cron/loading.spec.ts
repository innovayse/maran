import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubCronEntries, stubCronEntriesProblem, stubCronEnvironment } from '../fixtures/stub-cron-routes'
import type { Account } from '../../src/types/account'
import type { CronEntry } from '../../src/types/cronEntry'
import type { PanelModule } from '../../src/types/module'

// The cron entries while an account's crontab is on the way (#243): drawn from the table's own
// column schema.

const LICENSED: PanelModule[] = [{ name: 'cron', displayName: 'Scheduled tasks', tier: 'included', isEnabled: true }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const NIGHTLY: CronEntry = {
  entryId: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  schedule: { minute: '30', hour: '3', dayOfMonth: '*', month: '*', dayOfWeek: '2' },
  command: '/usr/bin/php /home/alice/cleanup.php',
  enabled: true,
}

/**
 * Signs in with the cron module licensed, one account and an empty environment.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubCronEnvironment(page, [])
}

describeTableLoading('cron entries', {
  stub: async (page) => {
    await signIn(page)
    await stubCronEntries(page, [NIGHTLY])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubCronEntriesProblem(page, 'The crontab could not be read.')
  },
  pattern: /\/api\/v1\/cron-entries(\?.*)?$/,
  path: '/cron',
  table: 'Scheduled tasks',
  rowText: NIGHTLY.command,
  failureText: 'The crontab could not be read.',
})
