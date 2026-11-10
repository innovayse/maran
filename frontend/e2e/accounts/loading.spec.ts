import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { test } from '../fixtures/guarded-test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubAccountStatuses } from '../fixtures/stub-account-statuses-route'
import { stubAccounts, stubAccountsProblem } from '../fixtures/stub-accounts-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// The accounts table while its page is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALPHA: Account = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '22222222-2222-2222-2222-222222222222',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/**
 * Signs in with the accounts module licensed and no plans.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubPlans(page, [])
}

/** How the accounts list screen is opened and answered. */
const ACCOUNTS_LIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubAccounts(page, [ALPHA])
  },
  stubFailure: async (page) => {
    await signIn(page)
    // The list page also asks which account states exist, whatever the list read answers.
    await stubAccountStatuses(page)
    await stubAccountsProblem(page, 'The accounts could not be read.')
  },
  pattern: /\/api\/v1\/accounts(\?.*)?$/,
  path: '/accounts',
  table: 'Hosting accounts',
  rowText: 'alpha.example.com',
  failureText: 'The accounts could not be read.',
}

test('accounts list: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, ACCOUNTS_LIST_SCENARIO)
})

test('accounts list: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, ACCOUNTS_LIST_SCENARIO)
})

test('accounts list: a slow failure is an alert, never a skeleton', async ({ page, guard }) => {
  // The refusal is the subject: the list read answers 500.
  guard.allow(500, /\/api\/v1\/accounts(\?.*)?$/)
  await expectTableSlowFailure(page, ACCOUNTS_LIST_SCENARIO)
})
