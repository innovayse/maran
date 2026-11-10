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
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteChoices, stubSites, stubSitesProblem } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'
import type { Page } from '@playwright/test'

// The sites table while its page is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  ownerName: 'alice',
  domain: 'alpha.example.com',
  backendType: 'php',
  backendTypeName: 'PHP',
  phpVersion: '8.3',
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

/**
 * Signs in with the sites module licensed, no PHP versions and the panel's default backend choices.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubPhpVersions(page, [])
  await stubSiteChoices(page)
}

/** How the sites list screen is opened and answered. */
const SITES_LIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubSites(page, [ALPHA])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubSitesProblem(page, 'The sites could not be read.')
  },
  pattern: /\/api\/v1\/sites(\?.*)?$/,
  path: '/sites',
  table: 'Websites',
  rowText: 'alpha.example.com',
  failureText: 'The sites could not be read.',
}

test('sites list: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, SITES_LIST_SCENARIO)
})

test('sites list: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, SITES_LIST_SCENARIO)
})

test('sites list: a slow failure is an alert, never a skeleton', async ({ page, guard }) => {
  guard.allow(500, /\/api\/v1\/sites(\?.*)?$/)
  await expectTableSlowFailure(page, SITES_LIST_SCENARIO)
})
