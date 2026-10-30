import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubAccounts, stubAccountsProblem } from '../fixtures/stub-accounts-route'
import { stubPlans } from '../fixtures/stub-plans-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'

// The accounts table while its page is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'accounts', displayName: 'Accounts', tier: 'included', isEnabled: true }]

const ALPHA: Account = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '22222222-2222-2222-2222-222222222222',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
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

test('accounts list: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, ACCOUNTS_LIST_SCENARIO)
})
