import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubMailChoices, stubMailDomains, stubMailRefusal, type MailStubRecorder } from '../fixtures/stub-mail-routes'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubMyAccount } from '../fixtures/stub-my-account-route'
import type { Account, MyAccount } from '../../src/types/account'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { MailDnsRecord, MailDomain } from '../../src/types/mailDomain'

// The mail domains page (#177): one server page of domains at a time, the add form, and the DNS
// records a domain needs published, in a dialog off its row. The panel computes the records and
// publishes none of them; the SPA only shows them and filters nothing itself.

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
  mailboxCount: 3,
  createdAt: '2026-08-01T10:00:00Z',
}

const BETA: MailDomain = {
  ...ALPHA,
  id: '22222222-aaaa-aaaa-aaaa-222222222222',
  name: 'beta.example.com',
  status: 'suspended',
  statusName: 'Suspended',
  mailboxCount: 0,
}

const RECORDS: MailDnsRecord[] = [
  { type: 'MX', name: '@', value: 'mail.panel.example.net', priority: 10, purposeName: 'Mail delivery' },
  { type: 'TXT', name: '@', value: 'v=spf1 mx ~all', priority: null, purposeName: 'Sender policy (SPF)' },
  { type: 'TXT', name: 'maran1._domainkey', value: 'v=DKIM1; k=rsa; p=MIIBIjANBgkq', priority: null, purposeName: 'DKIM signing key' },
]

/**
 * Signs in and serves the given mail domains.
 * @param page The Playwright page.
 * @param domains The domains the panel reports.
 * @param user The signed-in user; the administrator when omitted.
 * @returns The recorder of the list requests and writes.
 */
const openWith = async (page: Page, domains: MailDomain[], user?: AuthenticatedUser): Promise<MailStubRecorder> => {
  await stubSignedIn(page, user)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubAccounts(page, [ALICE])
  await stubAccountNames(page, [ALICE])
  await stubMyAccount(page, OWN_ACCOUNT)
  await stubMailChoices(page)
  return await stubMailDomains(page, domains, RECORDS)
}

/**
 * Opens a row's actions menu and picks an item.
 * @param page The Playwright page.
 * @param domain The domain's name.
 * @param item The menu item's name.
 * @returns Resolves once the item was picked.
 */
const pickRowAction = async (page: Page, domain: string, item: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${domain}` }).click()
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

test('the page shows the empty state when the panel reports no mail domain', async ({ page }) => {
  await openWith(page, [])

  await page.goto('/mail/domains')

  await expect(page.getByRole('heading', { level: 1, name: 'Mail domains' })).toBeVisible()
  await expect(page.getByText('No mail domains yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a row shows the domain, its state and mailbox count, and names its owner rather than its identifier', async ({ page }) => {
  await openWith(page, [ALPHA, BETA])

  await page.goto('/mail/domains')

  const row = page.getByRole('row').filter({ hasText: 'alpha.example.com' })
  await expect(row).toContainText('Active')
  await expect(row).toContainText('alice')
  await expect(row).not.toContainText(ALICE.id)
  await expect(page.getByRole('row').filter({ hasText: 'beta.example.com' })).toContainText('Suspended')
  await expect(page.getByRole('columnheader', { name: 'Account' })).toBeVisible()
})

test('a customer sees no account column', async ({ page }) => {
  await openWith(page, [ALPHA], CUSTOMER)

  await page.goto('/mail/domains')

  // Positive control: the table is drawn before a column's absence is judged.
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Domain' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Account' })).toHaveCount(0)
})

test('a customer adds a domain for their own account, and it joins the list', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/domains')

  await page.getByRole('textbox', { name: 'Domain' }).fill('gamma.example.com')
  await page.getByRole('button', { name: 'Add domain' }).click()

  await expect(page.getByText('Mail domain gamma.example.com added.')).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'gamma.example.com' })).toBeVisible()
  // The account is the caller's own, taken from the token; a customer is never asked for one.
  expect(recorder.writes()).toEqual([
    { method: 'POST', path: '/api/v1/mail-domains', body: { accountId: OWN_ACCOUNT.id, name: 'gamma.example.com' } },
  ])
})

test('an empty domain name is refused without asking the panel', async ({ page }) => {
  const recorder = await openWith(page, [], CUSTOMER)
  await page.goto('/mail/domains')

  await page.getByRole('button', { name: 'Add domain' }).click()

  await expect(page.getByText('Enter the domain name.')).toBeVisible()
  expect(recorder.writes()).toEqual([])
})

// A domain that is not a hostname of the account's own sites is refused by the panel, and the
// refusal is the backend's sentence, already localized, with nothing of the SPA's added to it.
test('a domain the panel refuses shows the backend sentence verbatim and the list stays', async ({ page }) => {
  const detail = 'The domain is not a hostname of one of your sites.'
  await openWith(page, [ALPHA], CUSTOMER)
  await stubMailRefusal(page, 'mail-domains', 'POST', detail)
  await page.goto('/mail/domains')
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toBeVisible()

  await page.getByRole('textbox', { name: 'Domain' }).fill('foreign.example.org')
  await page.getByRole('button', { name: 'Add domain' }).click()

  await expect(page.getByText(detail)).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'foreign.example.org' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toBeVisible()
})

test('the DNS records dialog lists what to publish, with a copy control per record', async ({ page }) => {
  await openWith(page, [ALPHA])
  await page.goto('/mail/domains')

  await pickRowAction(page, 'alpha.example.com', 'DNS records')

  const dialog = page.getByRole('dialog', { name: 'DNS records for alpha.example.com' })
  await expect(dialog.getByText('This panel does not publish them for you.', { exact: false })).toBeVisible()
  await expect(dialog.getByRole('row').filter({ hasText: 'Mail delivery' })).toContainText('MX 10')
  await expect(dialog.getByRole('row').filter({ hasText: 'DKIM signing key' })).toContainText('maran1._domainkey')
  await expect(dialog.getByText('v=spf1 mx ~all')).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Copy the value of maran1._domainkey' })).toBeVisible()

  await dialog.getByRole('button', { name: 'Done' }).click()
  await expect(dialog).toHaveCount(0)
})

test('rotating the DKIM key asks first and then reports the new key', async ({ page }) => {
  const recorder = await openWith(page, [ALPHA])
  await page.goto('/mail/domains')

  await pickRowAction(page, 'alpha.example.com', 'Rotate DKIM key')
  await expect(page.getByRole('dialog', { name: 'Rotate the DKIM key of alpha.example.com' })).toBeVisible()
  expect(recorder.writes()).toEqual([])

  await page.getByRole('button', { name: 'Rotate key' }).click()

  await expect(page.getByText('A new DKIM key was started for alpha.example.com.', { exact: false })).toBeVisible()
  expect(recorder.writes().map((write) => {return write.path})).toEqual(['/api/v1/mail-domains/11111111-1111-1111-1111-111111111111/dkim-rotation'])
})

test('removing a domain asks first, then drops the row', async ({ page }) => {
  const recorder = await openWith(page, [ALPHA, BETA])
  await page.goto('/mail/domains')

  await pickRowAction(page, 'beta.example.com', 'Remove')
  await expect(page.getByRole('dialog', { name: 'Remove mail domain beta.example.com' })).toBeVisible()
  // Cancelling is the inverse control: nothing was sent by asking.
  await page.getByRole('button', { name: 'Cancel' }).click()
  expect(recorder.writes()).toEqual([])
  await expect(page.getByRole('row').filter({ hasText: 'beta.example.com' })).toBeVisible()

  await pickRowAction(page, 'beta.example.com', 'Remove')
  await page.getByRole('button', { name: 'Remove domain' }).click()

  await expect(page.getByRole('row').filter({ hasText: 'beta.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toBeVisible()
  expect(recorder.writes().map((write) => {return write.method})).toEqual(['DELETE'])
})

test('a search reaches the URL, asks the panel, and narrows the rows', async ({ page }) => {
  const recorder = await openWith(page, [ALPHA, BETA])
  await page.goto('/mail/domains')

  await page.getByRole('searchbox', { name: 'Search mail domains' }).fill('beta')

  await expect(page).toHaveURL(/\/mail\/domains\?search=beta$/)
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'beta.example.com' })).toBeVisible()
  expect(recorder.lists().some((url) => {return url.searchParams.get('search') === 'beta'})).toBe(true)
})

test('the state filter in the sheet narrows on the server and becomes a removable chip', async ({ page }) => {
  const recorder = await openWith(page, [ALPHA, BETA])
  await page.goto('/mail/domains')

  await page.getByTestId('filter-trigger').click()
  const sheet = page.getByRole('dialog', { name: 'Filters' })
  await sheet.getByRole('combobox', { name: 'State' }).click()
  await page.getByRole('option', { name: 'Suspended' }).click()
  await sheet.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/status=suspended/)
  expect(recorder.lists().some((url) => {return url.searchParams.get('status') === 'suspended'})).toBe(true)
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toHaveCount(0)
  await expect(page.getByTestId('active-filter-chips')).toContainText('State: Suspended')

  await page.getByRole('button', { name: 'Remove filter: State: Suspended' }).click()
  await expect(page).not.toHaveURL(/status=/)
  await expect(page.getByRole('row').filter({ hasText: 'alpha.example.com' })).toBeVisible()
})
