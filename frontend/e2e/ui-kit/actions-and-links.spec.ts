import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubBackups } from '../fixtures/stub-backups-routes'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSftpUsers } from '../fixtures/stub-sftp-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { Backup } from '../../src/types/backup'
import type { Ban, WhitelistEntry } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

// Issue #218, points 6-10: a confirmation's button names its action, a create form empties after an
// accepted create, a way back looks like a link, a backup's reason is called what it is, and the
// upgrade screen never shows a module's machine id.

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  createdAt: '2026-08-01T10:00:00Z',
}

const SITE: SiteDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  domain: 'alice.example.com',
  aliases: [],
  backendType: 'php',
  phpVersion: '8.3',
  proxyUpstream: '',
  documentRoot: '/home/alice/sites/alice.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const BAN: Ban = {
  id: '00000000-0000-0000-0000-0000000000b2',
  ipAddress: '203.0.113.9',
  reason: 'manual',
  failures: 0,
  bannedAt: '2026-09-01T09:05:00+00:00',
  expiresAt: null,
}

const EXEMPTION: WhitelistEntry = {
  id: '00000000-0000-0000-0000-0000000000c1',
  cidr: '198.51.100.0/24',
  note: 'The office',
  createdAt: '2026-09-02T17:40:00+00:00',
}

const MANUAL_BACKUP: Backup = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  orphanedAccountUsername: '',
  status: 'completed',
  kind: 'manual',
  sizeBytes: 1_572_864,
  sha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
  databaseCount: 2,
  startedAt: '2026-09-06T03:00:00Z',
  finishedAt: '2026-09-06T03:04:00Z',
  failureCode: '',
  failureDisplayName: '',
}

/**
 * Signs an administrator in with the given modules licensed.
 * @param page The Playwright page.
 * @param modules The licensed modules.
 * @returns Resolves once the routes are installed.
 */
const signIn = async (page: Page, modules: PanelModule[]): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, modules)
  await stubAccounts(page, [ALICE])
}

/**
 * Asserts a confirmation answers with the named action and never with a bare "Yes".
 * @param dialog The open confirmation.
 * @param action The label its confirm button must carry.
 * @returns Resolves once the assertions have run.
 */
const expectNamedAnswer = async (dialog: Locator, action: string): Promise<void> => {
  await expect(dialog.getByRole('button', { name: action, exact: true })).toBeVisible()
  const answers = await dialog.getByRole('button').allInnerTexts()
  for (const answer of answers) {
    expect(answer.trim()).not.toMatch(/^Yes\b/)
  }
}

test('every confirmation names its action on the button that performs it', async ({ page }) => {
  await signIn(page, [
    { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
    { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  ])
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
  await stubFirewall(page, { rules: [], bans: [{ ...BAN }], whitelist: [{ ...EXEMPTION }] })

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()
  await expectNamedAnswer(page.getByRole('dialog', { name: `Delete site ${SITE.domain}` }), 'Delete site')

  await page.goto('/firewall/bans')
  await page.getByRole('button', { name: `Actions for ${BAN.ipAddress}` }).click()
  await page.getByRole('menuitem', { name: 'Lift the ban' }).click()
  await expectNamedAnswer(page.getByRole('dialog', { name: `Lift the ban on ${BAN.ipAddress}` }), 'Lift ban')

  await page.goto('/firewall/whitelist')
  await page.getByRole('button', { name: `Actions for ${EXEMPTION.cidr}` }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await expectNamedAnswer(
    page.getByRole('dialog', { name: `Remove the exemption for ${EXEMPTION.cidr}` }),
    'Remove exemption',
  )
})

test('an accepted SFTP login empties the create form, so the same submit is not invited twice', async ({ page }) => {
  await signIn(page, [
    { name: 'sftp', displayName: 'File transfer', tier: 'included', isEnabled: true },
    { name: 'ftp', displayName: 'FTPS', tier: 'included', isEnabled: true },
  ])
  await stubSftpUsers(page, [])
  await page.goto('/file-transfer/sftp')

  const account = page.getByRole('combobox', { name: 'Account' })
  const login = page.getByRole('textbox', { name: 'Login name' })
  await account.click()
  await page.getByRole('option', { name: /alice/ }).click()
  await login.fill('web')
  await expect(account).toContainText('alice')
  await page.getByRole('button', { name: 'Create SFTP login' }).click()
  await page.getByRole('button', { name: 'Done' }).click()

  await expect(login).toHaveValue('')
  // The picker is a trigger button, so empty is its placeholder showing instead of the account.
  await expect(account).not.toContainText('alice')
})

test('an accepted backup and an accepted ban each empty their form', async ({ page }) => {
  await signIn(page, [
    { name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true },
    { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
  ])
  await stubBackups(page, [])
  await stubFirewall(page, { rules: [], bans: [], whitelist: [] })

  await page.goto('/backups')
  const account = page.getByRole('combobox', { name: 'Account' })
  await account.click()
  await page.getByRole('option', { name: /alice/ }).click()
  await expect(account).toContainText('alice')
  await page.getByRole('button', { name: 'Take a backup' }).click()
  await expect(account).not.toContainText('alice')

  await page.goto('/firewall/bans')
  const address = page.getByRole('textbox', { name: 'Address' })
  await address.fill('192.0.2.44')
  await page.getByRole('button', { name: 'Ban the address' }).click()
  await expect(page.getByRole('row').filter({ hasText: '192.0.2.44' })).toBeVisible()
  await expect(address).toHaveValue('')
})

for (const width of [1440, 390]) {
  test(`at ${width}px the way back from a site is an underlined link to the list`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await signIn(page, [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }])
    await stubSites(page, [])
    await stubSiteDetail(page, SITE)
    await stubPhpVersions(page, [{ version: '8.3', isDefault: true }])
    await page.goto(`/sites/${SITE.id}`)

    const back = page.getByRole('link', { name: 'Back to sites' })
    await expect(back).toBeVisible()
    await expect(back).toHaveAttribute('href', '/sites')
    await expect(back).toHaveCSS('text-decoration-line', 'underline')
  })
}

test('a backup an operator took by hand reads "Manual" in the history', async ({ page }) => {
  await signIn(page, [{ name: 'backups', displayName: 'Backups', tier: 'included', isEnabled: true }])
  await stubBackups(page, [{ ...MANUAL_BACKUP }])
  await page.goto('/backups')

  const row = page.getByRole('row').filter({ hasText: ALICE.name })
  await expect(row.getByRole('cell', { name: 'Manual', exact: true })).toBeVisible()
  await expect(page.locator('body')).not.toContainText('Asked for')
})

test('the upgrade screen names a module by its display name, never by its id', async ({ page }) => {
  await signIn(page, [{ name: 'mail', displayName: 'Mail', tier: 'business', isEnabled: false }])
  await page.goto('/upgrade/mail')

  await expect(page.getByText('The "Mail" module is not included in your current licence.')).toBeVisible()
  await expect(page.locator('body')).not.toContainText('"mail"')
  // The card says how to get the module and offers the way back, not a lone line in an empty box.
  await expect(page.getByRole('heading', { name: 'How to get it' })).toBeVisible()
  await expect(page.getByText(/^The licence is managed by whoever administers this server/)).toBeVisible()
  await expect(page.getByRole('link', { name: 'Back to system status' })).toBeVisible()
})

test('an upgrade link to a module the catalogue does not know gets a sentence without the id', async ({ page }) => {
  await signIn(page, [])
  await page.goto('/upgrade/mail')

  await expect(page.getByText('This module is not included in your current licence.')).toBeVisible()
  await expect(page.locator('body')).not.toContainText('"mail"')
})
