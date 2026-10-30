import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabaseGrants, stubDatabaseGrantsProblem } from '../fixtures/stub-database-grant-routes'
import { stubDatabases, stubDatabasesProblem } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { Database } from '../../src/types/database'
import type { PanelModule } from '../../src/types/module'

// The databases table and the grant repair's rewritten rows while their reads are on the way
// (#243): each drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const SHOP: Database = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'shop',
  fullName: 'alice_shop',
  dbUserName: 'alice_shopuser',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Signs in with the databases module licensed and one account to pick.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
}

describeTableLoading('databases list', {
  stub: async (page) => {
    await signIn(page)
    await stubDatabases(page, [SHOP])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubDatabasesProblem(page, 'The databases could not be read.')
  },
  pattern: /\/api\/v1\/databases(\?.*)?$/,
  path: '/databases',
  table: 'MySQL databases',
  rowText: 'alice_shopuser',
  failureText: 'The databases could not be read.',
})

describeTableLoading('grant repair', {
  stub: async (page) => {
    await signIn(page)
    await stubDatabaseGrants(page, {
      isReportOnly: true,
      examinedGrants: 2,
      alreadyCorrect: 1,
      repaired: [],
      wouldRepair: [{ databaseName: 'alice_shop', dbUsername: 'alice_shopuser', alsoMatchedDatabases: [] }],
      refused: [],
    })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubDatabaseGrantsProblem(page, 'The server agent could not be reached.')
  },
  pattern: '**/api/v1/database-grants',
  path: '/databases/grant-repair',
  table: 'What a repair would narrow',
  rowText: 'alice_shopuser',
  failureText: 'The server agent could not be reached.',
})
