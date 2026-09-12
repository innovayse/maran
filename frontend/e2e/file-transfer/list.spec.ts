import { expect, test } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubFtpUsers, stubFtpsServer, runningFtpsStatus } from '../fixtures/stub-ftp-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers, stubSftpUsersProblem } from '../fixtures/stub-sftp-routes'
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

test('the file transfer screen shows the empty state when neither module reports a login', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])

  await page.goto('/file-transfer')

  await expect(page.getByText('No file transfer logins yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

// The screen's reason for existing: one account holds logins of both daemons, and one table is the
// only place an operator can see all of them at once.
test('one table lists the logins of both transfer daemons', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [WEB])
  await stubFtpUsers(page, [UPLOAD])

  await page.goto('/file-transfer')

  await expect(page.getByRole('table')).toHaveCount(1)
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'alice_upload' })).toBeVisible()
})

// Somebody who reads `web` here and types `web` into a client cannot log in; `alice_web` is what
// the host holds in /etc/passwd, and the SPA never assembles that name itself.
test('the panel shows the prefixed login so the operator can sign in with it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [WEB])
  await stubFtpUsers(page, [])

  await page.goto('/file-transfer')

  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'web', exact: true })).toHaveCount(0)
  await expect(
    page.getByText('SFTP and FTPS logins are separate users on this server.', { exact: false }),
  ).toBeVisible()
})

test('the row names the account that owns the login rather than printing its identifier', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [WEB])
  await stubFtpUsers(page, [])

  await page.goto('/file-transfer')

  const row = page.getByRole('row').filter({ hasText: 'alice_web' })
  await expect(row).toContainText('alice')
  await expect(row).not.toContainText(ALICE.id)
})

// rules/vue.md: "Error messages are produced by the backend, already localized, and rendered as-is."
//
// This is also where a REFUSAL lands. When the agent refuses because the account is busy, the wire
// carries no code that tells it apart from a host failure, so the screen renders the server's own
// sentence and states no distinction of its own — asserted by the absence of any SPA-authored
// failure copy beside it.
test('a failed list renders the backend RFC 7807 detail verbatim and adds no copy of its own', async ({
  page,
}) => {
  const backendDetail = 'The account is busy with another operation. Try again in a moment.'
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsersProblem(page, backendDetail)
  await stubFtpUsers(page, [])

  await page.goto('/file-transfer')

  await expect(page.getByText(backendDetail)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  await expect(page.getByText('No file transfer logins yet')).toHaveCount(0)
})

// Horizontal space is the scarce thing on a phone. The table is allowed to scroll — inside its own
// container — but the page behind it must not.
test('the table scrolls inside its own container rather than moving the page sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 780 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [WEB])
  await stubFtpUsers(page, [UPLOAD])

  await page.goto('/file-transfer')
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()

  const document = await page.evaluate(() => {
    const root = window.document.documentElement
    return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth }
  })
  expect(document.scrollWidth).toEqual(document.clientWidth)
})

test('the sidebar links to the merged screen when the panel licenses the module', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])

  await page.goto('/')
  await page.getByRole('navigation').getByRole('link', { name: 'File transfer', exact: true }).click()

  await expect(page).toHaveURL(/\/file-transfer$/)
  await expect(page.getByRole('heading', { level: 1, name: 'File transfer' })).toBeVisible()
})

// A customer's bookmark and every link the panel has ever rendered point at the old path. It stays
// and forwards rather than turning into a 404 on the day the merged screen shipped.
test('the old sftp-users bookmark lands on the merged screen', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [WEB])
  await stubFtpUsers(page, [])

  await page.goto('/sftp-users')

  await expect(page).toHaveURL(/\/file-transfer$/)
  await expect(page.getByRole('heading', { level: 1, name: 'File transfer' })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'alice_web' })).toBeVisible()
})

// The FTPS module contributes no sidebar entry: its whole interface is this screen. Two entries
// leading to one room is the defect this asserts against.
test('the licensed ftp module adds no second sidebar entry of its own', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])

  await page.goto('/')

  const navigation = page.getByRole('navigation')
  await expect(navigation.getByRole('link', { name: 'File transfer', exact: true })).toHaveCount(1)
  await expect(navigation.getByRole('link', { name: 'FTPS', exact: true })).toHaveCount(0)
})

// The sidebar entry that opens this screen is labelled with the panel's own words for the module,
// rendered verbatim, and the SPA adds no protocol name of its own to it. That matters because the
// screen covers two protocols: an entry reading SFTP or FTPS tells a customer the other one lives
// somewhere else.
//
// UNOBSERVED HERE: the words themselves. `stubModules` supplies `displayName`, so this spec cannot
// see what `Maran.Modules.Sftp/Resources/DisplayNames*.resx` actually holds — only that whatever
// the panel sends is what the sidebar shows, unaltered. The resx value is the backend's to assert.
test('the sidebar entry for this screen shows the panel word for word and names no protocol', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })
  await stubFtpsServer(page, runningFtpsStatus)
  await stubSftpUsers(page, [])
  await stubFtpUsers(page, [])

  await page.goto('/')

  const navigation = page.getByRole('navigation')
  const entry = navigation.getByRole('link', { name: 'File transfer', exact: true })

  // Positive control first: without it every count-of-zero below would also pass on a sidebar that
  // rendered nothing at all, which is the state this page is in while the catalogue is loading.
  await expect(entry).toHaveCount(1)
  await expect(entry).toHaveText('File transfer')

  // Loose on purpose, and only here: `getByRole(name:)` matches a case-insensitive SUBSTRING, so
  // these two catch a label that merely CONTAINS a protocol name — "SFTP logins", "sftp" — as well
  // as one that is exactly it.
  await expect(navigation.getByRole('link', { name: 'SFTP' })).toHaveCount(0)
  await expect(navigation.getByRole('link', { name: 'FTPS' })).toHaveCount(0)
})
