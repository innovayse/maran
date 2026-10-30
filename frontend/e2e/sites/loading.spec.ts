import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSites, stubSitesProblem } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { Site } from '../../src/types/site'

// The sites table while its page is on the way (#243): drawn from its own column schema.

const LICENSED: PanelModule[] = [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }]

const ALPHA: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  ownerName: 'alice',
  domain: 'alpha.example.com',
  backendType: 'php',
  phpVersion: '8.3',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Signs in with the sites module licensed and no PHP versions.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubPhpVersions(page, [])
}

describeTableLoading('sites list', {
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
})
