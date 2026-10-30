import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { runningFtpsStatus, stubFtpUsers, stubFtpsServer } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { FtpUser } from '../../src/types/ftpUser'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

// One module, one page (#196). SFTP and FTPS logins are different system users answered by
// different modules; each page reads its own module's endpoint and shows nothing of the other's. A
// page that still merged the two would show the other protocol's row here, which a customer would
// read as a login their client can use.

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
 * Signs in with both modules holding one login each.
 * @param page The Playwright page.
 * @returns The list requests each endpoint received so far.
 */
const signIn = async (page: Page): Promise<{ sftp: () => readonly URL[]; ftp: () => readonly URL[] }> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  const sftp = await stubSftpUsers(page, [WEB])
  const ftp = await stubFtpUsers(page, [UPLOAD])
  return { sftp, ftp }
}

test('the SFTP page lists only SFTP logins and asks only the sftp endpoint', async ({ page }) => {
  const requests = await signIn(page)

  await page.goto('/file-transfer/sftp')

  // Positive control first: the page's own row is drawn before the other module's absence counts.
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(page.getByText('alice_upload')).toHaveCount(0)
  expect(requests.sftp().length).toBeGreaterThan(0)
  expect(requests.ftp()).toHaveLength(0)
})

test('the FTPS page lists only FTPS logins and asks only the ftp endpoint', async ({ page }) => {
  const requests = await signIn(page)

  await page.goto('/file-transfer/ftps')

  await expect(page.getByRole('row').filter({ hasText: 'alice_upload' })).toBeVisible()
  await expect(page.getByText('alice_web')).toHaveCount(0)
  expect(requests.ftp().length).toBeGreaterThan(0)
  expect(requests.sftp()).toHaveLength(0)
})
