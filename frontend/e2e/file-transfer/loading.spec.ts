import { test, type Page } from '@playwright/test'
import {
  expectTableFastLoad,
  expectTableSlowFailure,
  expectTableSlowLoad,
  type TableLoadingScenario,
} from '../fixtures/expect-table-loading'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubFtpUsers, stubFtpsServer, runningFtpsStatus } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers, stubSftpUsersProblem } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

// The file transfer logins while they are on the way (#243): drawn from the table's own column
// schema. The SFTP read is the one held; the FTPS read answers at once.

const LICENSED: PanelModule[] = [
  { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
  { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const WEB: SftpUser = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'web',
  fullName: 'alice_web',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Signs in with both transfer modules licensed, no FTPS logins and a running FTPS server.
 * @param page The Playwright page.
 * @returns Resolves once the stubs are installed.
 */
const signIn = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubFtpUsers(page, [])
}

/** How the file transfer logins screen is opened and answered. */
const FILE_TRANSFER_LOGINS_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubSftpUsers(page, [WEB])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubSftpUsersProblem(page, 'The logins could not be read.')
  },
  pattern: '**/api/v1/sftp-users',
  path: '/file-transfer',
  table: 'SFTP and FTPS logins',
  rowText: 'alice_web',
  failureText: 'The logins could not be read.',
}

test('file transfer logins: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, FILE_TRANSFER_LOGINS_SCENARIO)
})

test('file transfer logins: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, FILE_TRANSFER_LOGINS_SCENARIO)
})

test('file transfer logins: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, FILE_TRANSFER_LOGINS_SCENARIO)
})
