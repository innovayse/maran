import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubMyAccountResources } from '../fixtures/stub-account-resources-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import {
  stubMailboxes,
  stubMailChoices,
  stubMailDomains,
  stubMailRefusal,
  type MailStubRecorder,
} from '../fixtures/stub-mail-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AccountResourceSummary } from '../../src/types/accountResourceSummary'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { MailDomain } from '../../src/types/mailDomain'
import type { Mailbox } from '../../src/types/mailbox'

// The mailboxes page (#177): one server page of mailboxes at a time, the create form (the domain
// picked by search, the password typed by the owner and never shown back), the quota each mailbox
// uses, and the limits and password dialogs. The plan's allowance is advice; the endpoint enforces.

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

const INFO: Mailbox = {
  id: 'aaaaaaaa-0000-0000-0000-000000000001',
  accountId: ALICE.id,
  ownerName: ALICE.name,
  mailDomainId: ALPHA.id,
  address: 'info@alpha.example.com',
  quotaMib: 1024,
  usedMib: 120,
  sendLimitPerHour: 100,
  status: 'active',
  statusName: 'Active',
  enabled: true,
  createdAt: '2026-08-01T10:00:00Z',
}

const SALES: Mailbox = {
  ...INFO,
  id: 'aaaaaaaa-0000-0000-0000-000000000002',
  address: 'sales@alpha.example.com',
  quotaMib: 2048,
  usedMib: null,
  status: 'suspended',
  statusName: 'Suspended',
}

/**
 * Signs in and serves the given mailboxes, with one mail domain for the picker.
 * @param page The Playwright page.
 * @param mailboxes The mailboxes the panel reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns The recorder of the mailbox list requests and writes.
 */
const openWith = async (page: Page, mailboxes: Mailbox[], user?: AuthenticatedUser): Promise<MailStubRecorder> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMailChoices(page)
  await stubMailDomains(page, [ALPHA])
  return await stubMailboxes(page, mailboxes)
}

/**
 * Opens a row's actions menu and picks an item.
 * @param page The Playwright page.
 * @param address The mailbox's address.
 * @param item The menu item's name.
 * @returns Resolves once the item was picked.
 */
const pickRowAction = async (page: Page, address: string, item: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${address}` }).click()
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

/**
 * Fills the create form.
 * @param page The Playwright page.
 * @param password The password to type.
 * @returns Resolves once the form is filled.
 */
const fillForm = async (page: Page, password: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Domain' }).click()
  await page.getByRole('option', { name: 'alpha.example.com' }).click()
  await page.getByRole('textbox', { name: 'Name before the at sign' }).fill('support')
  await page.getByLabel('Password', { exact: true }).fill(password)
}

test('the page shows the empty state when the panel reports no mailbox', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/mail/mailboxes')

  await expect(page.getByRole('heading', { level: 1, name: 'Mailboxes' })).toBeVisible()
  await expect(page.getByText('No mailboxes yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a row shows the address, the state, the space used against the limit and the send limit', async ({ page }) => {
  await openWith(page, [INFO, SALES])

  await page.goto('/mail/mailboxes')

  const info = page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })
  await expect(info).toContainText('Active')
  await expect(info).toContainText('120 MiB of 1 GiB')
  await expect(info.getByRole('progressbar', { name: 'Space used by info@alpha.example.com' })).toBeVisible()
  await expect(info).toContainText('alice')
  // The mail host has not reported this mailbox's usage: the limit alone is said, with no bar that
  // would read as "nothing used".
  const sales = page.getByRole('row').filter({ hasText: 'sales@alpha.example.com' })
  await expect(sales).toContainText('Limit 2 GiB')
  await expect(sales.getByRole('progressbar')).toHaveCount(0)
  await expect(sales).toContainText('Suspended')
})

test('a customer sees no account column', async ({ page }) => {
  await openWith(page, [INFO], CUSTOMER)

  await page.goto('/mail/mailboxes')

  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Address' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Account' })).toHaveCount(0)
})

test('a mailbox is created in the chosen domain, with no account named and no password kept', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/mailboxes')

  await fillForm(page, 'Correct-Horse-9')
  await page.getByRole('button', { name: 'Create mailbox' }).click()

  await expect(page.getByText('Mailbox created.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'support@alpha.example.com' })).toBeVisible()
  // The account is the domain's own, so the request never names one; the quota is the panel's default.
  expect(recorder.writes()).toEqual([
    {
      method: 'POST',
      path: '/api/v1/mailboxes',
      body: { mailDomainId: ALPHA.id, localPart: 'support', password: 'Correct-Horse-9', quotaMib: 1024 },
    },
  ])
  // The form forgot the secret it sent: a form still holding it invites the same submit twice.
  await expect(page.getByLabel('Password', { exact: true })).toHaveValue('')
  await expect(page.getByText('Correct-Horse-9')).toHaveCount(0)
})

test('a password shorter than the panel states is refused without asking the panel', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/mailboxes')

  await fillForm(page, 'short')
  await page.getByRole('button', { name: 'Create mailbox' }).click()

  await expect(page.getByText('The password must be at least 10 characters.')).toBeVisible()
  expect(recorder.writes()).toEqual([])
})

test('a size limit outside the panel range is refused without asking the panel', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/mailboxes')

  await fillForm(page, 'Correct-Horse-9')
  await page.getByRole('textbox', { name: 'Size limit (MiB)' }).fill('99999')
  await page.getByRole('button', { name: 'Create mailbox' }).click()

  await expect(page.getByText('The size limit must be between 10 and 10240 MiB.')).toBeVisible()
  expect(recorder.writes()).toEqual([])
})

test('an address the panel refuses shows the backend sentence verbatim', async ({ page }) => {
  const detail = 'That address is already taken.'
  await openWith(page, [], CUSTOMER)
  await stubMailRefusal(page, 'mailboxes', 'POST', detail)
  await page.goto('/mail/mailboxes')

  await fillForm(page, 'Correct-Horse-9')
  await page.getByRole('button', { name: 'Create mailbox' }).click()

  await expect(page.getByText(detail)).toBeVisible()
})

// The plan's allowance is advice that saves a refused request; the endpoint enforces it regardless.
test('a plan with every mailbox in use withholds the create form and says why', async ({ page }) => {
  const used: AccountResourceSummary = {
    accountId: OWN_ACCOUNT.id,
    resources: [{ kind: 'mailboxes', count: { state: 'counted', used: 2 }, limit: { kind: 'limited', maximum: 2 } }],
  }
  await openWith(page, [INFO, SALES], CUSTOMER)
  await stubMyAccountResources(page, used)

  await page.goto('/mail/mailboxes')

  await expect(page.getByText('your plan allows 2, and all of them are in use', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create mailbox' })).toHaveCount(0)
  // Positive control: the list itself is still there.
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toBeVisible()
})

test('the limits dialog saves a new size limit, send limit and state', async ({ page }) => {
  const recorder = await openWith(page, [INFO])
  await page.goto('/mail/mailboxes')

  await pickRowAction(page, 'info@alpha.example.com', 'Edit limits')
  const dialog = page.getByRole('dialog', { name: 'Edit info@alpha.example.com' })
  await dialog.getByRole('textbox', { name: 'Size limit (MiB)' }).fill('2048')
  await dialog.getByRole('textbox', { name: 'Messages it may send per hour' }).fill('50')
  await dialog.getByRole('switch', { name: 'Mailbox switched on' }).click()
  await dialog.getByRole('button', { name: 'Save changes' }).click()

  await expect(page.getByText('info@alpha.example.com was updated.')).toBeVisible()
  expect(recorder.writes()).toEqual([
    {
      method: 'PUT',
      path: `/api/v1/mailboxes/${INFO.id}`,
      body: { quotaMib: 2048, sendLimitPerHour: 50, enabled: false },
    },
  ])
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toContainText('Switched off')
})

// The owner's choice and the panel's are different facts, and only the owner's is theirs to change.
test('a mailbox the panel suspended cannot be switched on from the limits dialog', async ({ page }) => {
  await openWith(page, [SALES])
  await page.goto('/mail/mailboxes')

  await pickRowAction(page, 'sales@alpha.example.com', 'Edit limits')

  const dialog = page.getByRole('dialog', { name: 'Edit sales@alpha.example.com' })
  await expect(dialog.getByRole('switch', { name: 'Mailbox switched on' })).toBeDisabled()
  await expect(dialog.getByText('The panel has suspended this mailbox', { exact: false })).toBeVisible()
})

test('the password dialog sends the new password once and shows it nowhere', async ({ page }) => {
  const recorder = await openWith(page, [INFO])
  await page.goto('/mail/mailboxes')

  await pickRowAction(page, 'info@alpha.example.com', 'Change password')
  const dialog = page.getByRole('dialog', { name: 'New password for info@alpha.example.com' })
  await dialog.getByLabel('New password', { exact: true }).fill('Another-Long-Pass-1')
  await dialog.getByRole('button', { name: 'Set password' }).click()

  await expect(page.getByText('The password of info@alpha.example.com was changed.')).toBeVisible()
  await expect(page.getByText('Another-Long-Pass-1')).toHaveCount(0)
  expect(recorder.writes()).toEqual([
    { method: 'PUT', path: `/api/v1/mailboxes/${INFO.id}/password`, body: { password: 'Another-Long-Pass-1' } },
  ])
})

test('removing a mailbox asks first, then drops the row', async ({ page }) => {
  const recorder = await openWith(page, [INFO, SALES])
  await page.goto('/mail/mailboxes')

  await pickRowAction(page, 'sales@alpha.example.com', 'Remove')
  await expect(page.getByRole('dialog', { name: 'Remove mailbox sales@alpha.example.com' })).toBeVisible()
  // Cancelling is the inverse control: nothing was sent by asking.
  await page.getByRole('button', { name: 'Cancel' }).click()
  expect(recorder.writes()).toEqual([])

  await pickRowAction(page, 'sales@alpha.example.com', 'Remove')
  await page.getByRole('button', { name: 'Remove mailbox' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'sales@alpha.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toBeVisible()
  expect(recorder.writes().map((write) => {return write.method})).toEqual(['DELETE'])
})

test('the domain and state filters narrow on the server and become removable chips', async ({ page }) => {
  const recorder = await openWith(page, [INFO, SALES])
  await page.goto('/mail/mailboxes')

  await page.getByTestId('filter-trigger').click()
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'Domain' }).click()
  await page.getByRole('option', { name: 'alpha.example.com' }).click()
  await sheet.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: 'Suspended' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/domain=alpha\.example\.com/)
  await expect(page).toHaveURL(/status=suspended/)
  expect(
    recorder.lists().some((url) => {
      return url.searchParams.get('domain') === 'alpha.example.com' && url.searchParams.get('status') === 'suspended'
    }),
  ).toBe(true)
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'sales@alpha.example.com' })).toBeVisible()
  const chips = page.getByTestId('active-filter-chips')
  await expect(chips).toContainText('Domain: alpha.example.com')
  await expect(chips).toContainText('State: Suspended')

  await page.getByRole('button', { name: 'Clear all' }).click()
  await expect(page).toHaveURL(/\/mail\/mailboxes$/)
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toBeVisible()
})

test('a search matching nothing is the no-results state, not the empty state', async ({ page }) => {
  await openWith(page, [INFO])

  await page.goto('/mail/mailboxes?search=nowhere')

  await expect(page.getByText('No mailboxes match')).toBeVisible()
  await expect(page.getByText('No mailboxes yet')).toHaveCount(0)

  await page.getByRole('button', { name: 'Clear search and filters' }).click()
  await expect(page).toHaveURL(/\/mail\/mailboxes$/)
  await expect(page.getByRole('row').filter({ hasText: 'info@alpha.example.com' })).toBeVisible()
})
