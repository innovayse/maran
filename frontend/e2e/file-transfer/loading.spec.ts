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
import { runningFtpsStatus, stubFtpUsers, stubFtpUsersProblem, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers, stubSftpUsersProblem } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { FtpUser } from '../../src/types/ftpUser'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

// The SFTP and FTPS logins while they are on the way (#243): each page's table drawn from its own
// column schema, the page's own list read being the one held.

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
  protocol: 'Sftp',
  createdAt: '2026-08-01T10:00:00Z',
}

const UPLOAD: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'upload',
  fullName: 'alice_upload',
  protocol: 'Ftps',
  createdAt: '2026-08-02T10:00:00Z',
}

/**
 * Signs in as the administrator with both transfer modules licensed and a running FTPS server.
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
}

/** How the SFTP logins screen is opened and answered. */
const SFTP_LOGINS_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubSftpUsers(page, [WEB])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubSftpUsersProblem(page, 'The logins could not be read.')
  },
  pattern: /\/api\/v1\/sftp-users(\?.*)?$/,
  path: '/file-transfer/sftp',
  table: 'SFTP logins',
  rowText: 'alice_web',
  failureText: 'The logins could not be read.',
}

test('SFTP logins: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, SFTP_LOGINS_SCENARIO)
})

test('SFTP logins: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, SFTP_LOGINS_SCENARIO)
})

test('SFTP logins: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, SFTP_LOGINS_SCENARIO)
})

/** How the FTPS logins screen is opened and answered. */
const FTPS_LOGINS_SCENARIO: TableLoadingScenario = {
  stub: async (page) => {
    await signIn(page)
    await stubFtpUsers(page, [UPLOAD])
  },
  stubFailure: async (page) => {
    await signIn(page)
    await stubFtpUsersProblem(page, 'The logins could not be read.')
  },
  pattern: /\/api\/v1\/ftp-users(\?.*)?$/,
  path: '/file-transfer/ftps',
  table: 'FTPS logins',
  rowText: 'alice_upload',
  failureText: 'The logins could not be read.',
}

test('FTPS logins: a slow answer shows placeholder rows under the real header, then the rows', async ({ page }) => {
  await expectTableSlowLoad(page, FTPS_LOGINS_SCENARIO)
})

test('FTPS logins: a fast answer shows no placeholder', async ({ page }) => {
  await expectTableFastLoad(page, FTPS_LOGINS_SCENARIO)
})

test('FTPS logins: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectTableSlowFailure(page, FTPS_LOGINS_SCENARIO)
})
