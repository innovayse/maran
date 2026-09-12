import { expect, test, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubFtpUsers, stubFtpsServer, runningFtpsStatus } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import type { Account } from '../../src/types/account'
import type { FtpUser } from '../../src/types/ftpUser'
import type { PanelModule } from '../../src/types/module'
import type { SftpUser } from '../../src/types/sftpUser'

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
  name: 'web',
  fullName: 'alice_web',
  createdAt: '2026-08-01T10:00:00Z',
}

const UPLOAD: FtpUser = {
  id: '33333333-3333-3333-3333-333333333333',
  accountId: ALICE.id,
  name: 'upload',
  fullName: 'alice_upload',
  protocol: 'Ftps',
  createdAt: '2026-08-02T10:00:00Z',
}

/**
 * Puts the merged screen in front of an administrator with the two lists the case needs.
 * @param page The Playwright page under test.
 * @param sftpUsers The SFTP logins the panel reports.
 * @param ftpUsers The FTPS logins the panel reports.
 * @returns Resolves once the screen has been opened.
 */
const openScreen = async (page: Page, sftpUsers: SftpUser[], ftpUsers: FtpUser[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, sftpUsers)
  await stubFtpUsers(page, ftpUsers)
  await page.goto('/file-transfer')
}

// The whole point of the merged screen. The two protocols are different system users on one host,
// so a row that named neither would be telling the customer either client would work.
//
// The protocol CELL is asserted by exact text rather than by a substring of the row: `SFTP` is a
// substring of nothing here, but `FTPS` is matched case-insensitively by `hasText`, so a row-level
// needle would pass against a table that had stopped rendering the column at all — the page heading
// and the sidebar both carry the word.
test('an SFTP row and an FTPS row are labelled with their own protocol', async ({ page }) => {
  await openScreen(page, [WEB], [UPLOAD])

  const sftpRow = page.getByRole('row').filter({ hasText: 'alice_web' })
  const ftpsRow = page.getByRole('row').filter({ hasText: 'alice_upload' })

  await expect(sftpRow.getByRole('cell').nth(1)).toHaveText('SFTP')
  await expect(ftpsRow.getByRole('cell').nth(1)).toHaveText('FTPS')
})

// The row's protocol comes from the response, not from which endpoint answered. A panel that sent a
// protocol this bundle does not know must render absence rather than guessing: an FTPS login shown
// under an SFTP label is a confident, wrong instruction to a customer.
test('a protocol this bundle does not recognise renders as absence rather than as either daemon', async ({
  page,
}) => {
  const FUTURE: FtpUser = { ...UPLOAD, protocol: 'FtpsOverQuic' }
  await openScreen(page, [], [FUTURE])

  const row = page.getByRole('row').filter({ hasText: 'alice_upload' })
  await expect(row.getByRole('cell').nth(1)).toHaveText('—')
})

// The `Sftp` module carries no protocol member on the wire yet. Absence has exactly one correct
// reading — that endpoint has only ever returned SFTP logins — and it is not the same answer as an
// unrecognised token above.
test('an SFTP row with no protocol on the wire is still labelled SFTP', async ({ page }) => {
  await openScreen(page, [WEB], [])

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await expect(row.getByRole('cell').nth(1)).toHaveText('SFTP')
})

test('the table has a protocol column and names it', async ({ page }) => {
  await openScreen(page, [WEB], [UPLOAD])

  await expect(page.getByRole('columnheader', { name: 'Protocol', exact: true })).toBeVisible()
})
