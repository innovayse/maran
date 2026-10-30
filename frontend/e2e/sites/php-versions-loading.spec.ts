import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPhpVersions } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'

// The installed PHP versions while the list is on the way (#243): drawn from the table's own
// column schema. The screen belongs to the sites module, so its spec lives beside the sites list's.

const LICENSED: PanelModule[] = [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }]

/** The PHP versions read; the screen's only request of its own. */
const PHP_VERSIONS = '**/api/v1/sites/php-versions'

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The PHP versions could not be read.'

/**
 * Signs in with the sites module licensed.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

describeTableLoading('PHP versions', {
  stub: async (page) => {
    await signIn(page)
    await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await page.route(PHP_VERSIONS, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: PHP_VERSIONS,
  path: '/settings/php',
  table: 'PHP versions installed on this server',
  rowText: '8.3',
  failureText: REFUSAL,
})
