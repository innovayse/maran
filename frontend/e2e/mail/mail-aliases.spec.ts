import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubMailAliases, stubMailChoices, stubMailDomains, type MailStubRecorder } from '../fixtures/stub-mail-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { MailAlias } from '../../src/types/mailAlias'
import type { MailDomain } from '../../src/types/mailDomain'

// The aliases and forwarders page (#177): aliases, forwarders to outside addresses and the catch-all
// of a domain, one server page at a time. Whether a destination is a legal address, an outside one
// or a loop is the server's to decide; the form only checks the shape and the count the panel states.

const LICENSED: PanelModule[] = [{ name: 'mail', displayName: 'Mail', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

const OWN_ACCOUNT: MyAccount = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'olive',
  primaryDomain: 'olive.example.com',
  status: 'active',
  statusName: 'Active',
  plan: {
    displayName: 'Starter',
    diskQuotaMb: 1024,
    maxSites: 5,
    maxDatabases: 5,
    maxSftpUsers: 5,
    maxFtpUsers: 5,
    maxCronEntries: 5,
  },
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: OWN_ACCOUNT.id,
}

const ALPHA: MailDomain = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  name: 'alpha.example.com',
  status: 'active',
  statusName: 'Active',
  dkimSelector: 'maran1',
  mailboxCount: 2,
  createdAt: '2026-08-01T10:00:00Z',
}

const TEAM: MailAlias = {
  id: 'bbbbbbbb-0000-0000-0000-000000000001',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  mailDomainId: ALPHA.id,
  address: 'team@alpha.example.com',
  kind: 'alias',
  kindName: 'Alias',
  targets: ['info@alpha.example.com', 'sales@alpha.example.com'],
  createdAt: '2026-08-01T10:00:00Z',
}

const OUTSIDE: MailAlias = {
  ...TEAM,
  id: 'bbbbbbbb-0000-0000-0000-000000000002',
  address: 'press@alpha.example.com',
  kind: 'forwarder',
  kindName: 'Forwarder',
  targets: ['editor@example.org'],
}

/**
 * Signs in and serves the given aliases, with one mail domain for the picker.
 * @param page The Playwright page.
 * @param aliases The aliases the panel reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns The recorder of the alias list requests and writes.
 */
const openWith = async (page: Page, aliases: MailAlias[], user?: AuthenticatedUser): Promise<MailStubRecorder> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMailChoices(page)
  await stubMailDomains(page, [ALPHA])
  return await stubMailAliases(page, aliases)
}

/**
 * Opens a row's actions menu and picks an item.
 * @param page The Playwright page.
 * @param address The alias's address.
 * @param item The menu item's name.
 * @returns Resolves once the item was picked.
 */
const pickRowAction = async (page: Page, address: string, item: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${address}` }).click()
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

/**
 * Picks the domain in the create form and chooses a type.
 * @param page The Playwright page.
 * @param kind The type's name as the panel labels it.
 * @returns Resolves once both are chosen.
 */
const chooseDomainAndKind = async (page: Page, kind: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Domain' }).click()
  await page.getByRole('option', { name: 'alpha.example.com' }).click()
  await page.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: kind, exact: true }).click()
}

test('the page shows the empty state when the panel reports no alias', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/mail/aliases')

  await expect(page.getByRole('heading', { level: 1, name: 'Forwarders and aliases' })).toBeVisible()
  await expect(page.getByText('No aliases or forwarders yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a row shows the address, what it is and where the mail goes', async ({ page }) => {
  await openWith(page, [TEAM, OUTSIDE])

  await page.goto('/mail/aliases')

  const team = page.getByRole('row').filter({ hasText: 'team@alpha.example.com' })
  await expect(team).toContainText('Alias')
  await expect(team).toContainText('info@alpha.example.com, sales@alpha.example.com')
  await expect(page.getByRole('row').filter({ hasText: 'press@alpha.example.com' })).toContainText('Forwarder')
  await expect(page.getByRole('row').filter({ hasText: 'press@alpha.example.com' })).toContainText('editor@example.org')
})

test('a forwarder is created with its destinations split one per line', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/aliases')

  await chooseDomainAndKind(page, 'Forwarder')
  await page.getByRole('textbox', { name: 'Name before the at sign' }).fill('press')
  await page.getByRole('textbox', { name: 'Deliver to (one address per line)' }).fill('editor@example.org\n\n  desk@example.org ')
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page.getByText('Alias created.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'press@alpha.example.com' })).toBeVisible()
  expect(recorder.writes()).toEqual([
    {
      method: 'POST',
      path: '/api/v1/mail-aliases',
      body: {
        mailDomainId: ALPHA.id,
        kind: 'forwarder',
        localPart: 'press',
        targets: ['editor@example.org', 'desk@example.org'],
      },
    },
  ])
})

// A catch-all takes what no other address claims, so it has no name of its own to ask for.
test('a catch-all asks for no name and sends none', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/aliases')

  await chooseDomainAndKind(page, 'Catch-all')
  await expect(page.getByRole('textbox', { name: 'Name before the at sign' })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Deliver to (one address per line)' }).fill('info@alpha.example.com')
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page.getByRole('row').filter({ hasText: '*@alpha.example.com' })).toBeVisible()
  expect(recorder.writes()).toHaveLength(1)
  expect(recorder.writes()[0]?.body).toEqual({
    mailDomainId: ALPHA.id,
    kind: 'catchAll',
    localPart: '',
    targets: ['info@alpha.example.com'],
  })
})

test('more destinations than the panel allows are refused without asking the panel', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/aliases')

  await chooseDomainAndKind(page, 'Alias')
  await page.getByRole('textbox', { name: 'Name before the at sign' }).fill('team')
  await page.getByRole('textbox', { name: 'Deliver to (one address per line)' }).fill('a@alpha.example.com\nb@alpha.example.com\nc@alpha.example.com\nd@alpha.example.com')
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page.getByText('An alias can deliver to at most 3 addresses.')).toBeVisible()
  expect(recorder.writes()).toEqual([])
})

test('an empty destination list is refused without asking the panel', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/aliases')

  await chooseDomainAndKind(page, 'Alias')
  await page.getByRole('textbox', { name: 'Name before the at sign' }).fill('team')
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page.getByText('Enter at least one address to deliver to.')).toBeVisible()
  expect(recorder.writes()).toEqual([])
})

test('editing replaces the destinations and leaves the address and type alone', async ({ page }) => {
  const recorder = await openWith(page, [TEAM])
  await page.goto('/mail/aliases')

  await pickRowAction(page, 'team@alpha.example.com', 'Change destinations')
  const dialog = page.getByRole('dialog', { name: 'Destinations of team@alpha.example.com' })
  await expect(dialog.getByRole('textbox', { name: 'Deliver to (one address per line)' })).toHaveValue(
    'info@alpha.example.com\nsales@alpha.example.com',
  )
  await dialog.getByRole('textbox', { name: 'Deliver to (one address per line)' }).fill('office@alpha.example.com')
  await dialog.getByRole('button', { name: 'Save destinations' }).click()

  await expect(page.getByText('team@alpha.example.com was updated.')).toBeVisible()
  expect(recorder.writes()).toEqual([
    { method: 'PUT', path: `/api/v1/mail-aliases/${TEAM.id}`, body: { targets: ['office@alpha.example.com'] } },
  ])
  await expect(page.getByRole('row').filter({ hasText: 'team@alpha.example.com' })).toContainText('office@alpha.example.com')
})

test('removing an alias asks first, then drops the row', async ({ page }) => {
  const recorder = await openWith(page, [TEAM, OUTSIDE])
  await page.goto('/mail/aliases')

  await pickRowAction(page, 'press@alpha.example.com', 'Remove')
  await expect(page.getByRole('dialog', { name: 'Remove press@alpha.example.com' })).toBeVisible()
  // Cancelling is the inverse control: nothing was sent by asking.
  await page.getByRole('button', { name: 'Cancel' }).click()
  expect(recorder.writes()).toEqual([])

  await pickRowAction(page, 'press@alpha.example.com', 'Remove')
  await page.getByRole('button', { name: 'Remove address' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'press@alpha.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'team@alpha.example.com' })).toBeVisible()
  expect(recorder.writes().map((write) => {return write.method})).toEqual(['DELETE'])
})

test('the type filter narrows on the server and becomes a removable chip', async ({ page }) => {
  const recorder = await openWith(page, [TEAM, OUTSIDE])
  await page.goto('/mail/aliases')

  await page.getByTestId('filter-trigger').click()
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Forwarder' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/kind=forwarder/)
  expect(recorder.lists().some((url) => {return url.searchParams.get('kind') === 'forwarder'})).toBe(true)
  await expect(page.getByRole('row').filter({ hasText: 'team@alpha.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'press@alpha.example.com' })).toBeVisible()
  await expect(page.getByTestId('active-filter-chips')).toContainText('Type: Forwarder')
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, [TEAM])

  await page.goto('/mail/aliases?search=nowhere')

  await expect(page.getByText('No aliases match')).toBeVisible()
  await expect(page.getByText('No aliases or forwarders yet')).toHaveCount(0)
})
