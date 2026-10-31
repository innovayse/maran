import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall, stubFirewallRefusal } from '../fixtures/stub-firewall-routes'
import type { Ban, WhitelistEntry } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

// The firewall's three tables while their lists are on the way (#243), each on its own page: the
// rules whole, the bans and the whitelist one server page at a time.

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }]

const BAN: Ban = {
  id: '00000000-0000-0000-0000-0000000000b1',
  ipAddress: '198.51.100.4',
  reason: 'bruteForce',
  failures: 12,
  bannedAt: '2026-09-01T11:45:00+00:00',
  expiresAt: null,
}

const EXEMPTION: WhitelistEntry = {
  id: '00000000-0000-0000-0000-0000000000a1',
  cidr: '192.0.2.0/24',
  note: 'The office',
  createdAt: '2026-08-01T10:00:00+00:00',
}

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

/** How the firewall rules screen is opened and answered. */
const FIREWALL_RULES_SCENARIO: TableLoadingScenario = {
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
}

test('firewall rules: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, FIREWALL_RULES_SCENARIO)
})

test('firewall rules: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, FIREWALL_RULES_SCENARIO)
})

test('firewall rules: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, FIREWALL_RULES_SCENARIO)
})

/** How the bans page is opened and answered. */
const FIREWALL_BANS_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubFirewall(page, { rules: [], bans: [BAN], whitelist: [] })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubFirewallRefusal(page, 502, 'The bans could not be read.')
  },
  pattern: '**/api/v1/firewall/bans*',
  path: '/firewall/bans',
  table: 'Banned addresses',
  rowText: BAN.ipAddress,
  failureText: 'The bans could not be read.',
}

test('firewall bans: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, FIREWALL_BANS_SCENARIO)
})

test('firewall bans: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, FIREWALL_BANS_SCENARIO)
})

test('firewall bans: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, FIREWALL_BANS_SCENARIO)
})

/** How the whitelist page is opened and answered. */
const FIREWALL_WHITELIST_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubFirewall(page, { rules: [], bans: [], whitelist: [EXEMPTION] })
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubFirewallRefusal(page, 502, 'The exemptions could not be read.')
  },
  pattern: /\/api\/v1\/firewall\/whitelist(\?.*)?$/,
  path: '/firewall/whitelist',
  table: 'Ranges exempt from the automatic bans',
  rowText: EXEMPTION.cidr,
  failureText: 'The exemptions could not be read.',
}

test('firewall whitelist: a slow answer shows placeholder rows under the real header, then the rows', async ({
  page,
}) => {
  await expectTableSlowLoad(page, FIREWALL_WHITELIST_SCENARIO)
})

test('firewall whitelist: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, FIREWALL_WHITELIST_SCENARIO)
})

test('firewall whitelist: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, FIREWALL_WHITELIST_SCENARIO)
})
