import type { Page } from '@playwright/test'
import { describeTableLoading } from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall, stubFirewallRefusal } from '../fixtures/stub-firewall-routes'
import type { PanelModule } from '../../src/types/module'

// The firewall's three tables while their lists are on the way (#243). The three are read
// together, so holding the rules holds the whole screen.

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }]

/**
 * Signs in as an administrator with the firewall module licensed.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
}

describeTableLoading('firewall rules', {
  stub: async (page) => {
    await signIn(page)
    await stubFirewall(page, {
      rules: [{ port: 8443, portTo: null, protocol: 'tcp', sourceCidr: '203.0.113.0/24' }],
      bans: [],
      whitelist: [],
    })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubFirewallRefusal(page, 502, 'The firewall could not be read.')
  },
  pattern: '**/api/v1/firewall/rules*',
  path: '/firewall',
  table: 'Firewall rules',
  rowText: '203.0.113.0/24',
  failureText: 'The firewall could not be read.',
})
