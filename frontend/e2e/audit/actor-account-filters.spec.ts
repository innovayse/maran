import { expect, test, type Page } from '@playwright/test'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'
import type { AuditEvent } from '../../src/types/audit'

// The journal's actor and account filters (#145). The actor is a typed login name the panel matches
// exactly; the account is picked from the panel's account choices and sent as its id. An entry
// carries an account only where the panel could attribute one, which the stub models with a map.

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

const BOB: Account = { ...ALICE, id: '55555555-5555-5555-5555-555555555555', name: 'bob', primaryDomain: 'bob.example.com' }

const ADMIN_SIGN_IN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000f1',
  occurredAt: '2026-09-10T09:00:00+00:00',
  actorUsername: 'admin',
  impersonatorUsername: null,
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'admin',
  ipAddress: '203.0.113.7',
  succeeded: true,
  outcomeName: 'Succeeded',
}

const ALICE_SIGN_IN: AuditEvent = {
  ...ADMIN_SIGN_IN,
  id: '00000000-0000-0000-0000-0000000000f2',
  actorUsername: 'alice',
  subject: 'alice',
  ipAddress: '198.51.100.4',
}

const BOB_SIGN_IN: AuditEvent = {
  ...ADMIN_SIGN_IN,
  id: '00000000-0000-0000-0000-0000000000f3',
  actorUsername: 'bob',
  subject: 'bob',
  ipAddress: '192.0.2.99',
}

/** Which account the panel attributed each entry to; the administrator's own sign-in has none. */
const ACCOUNT_OF: Record<string, string> = {
  [ALICE_SIGN_IN.id]: ALICE.id,
  [BOB_SIGN_IN.id]: BOB.id,
}

/**
 * Signs an administrator in over the three entries and the two accounts.
 * @param page The Playwright page.
 * @returns Every list request the page made, oldest first.
 */
const openJournal = async (page: Page): Promise<() => readonly URL[]> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  return await stubAudit(page, [ADMIN_SIGN_IN, ALICE_SIGN_IN, BOB_SIGN_IN], ACCOUNT_OF)
}

test('the actor typed in the sheet narrows the journal to that login, whatever the case', async ({ page }) => {
  const requests = await openJournal(page)

  await page.goto('/settings/audit')
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('textbox', { name: 'Who' }).fill('ALICE')
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(/actor=ALICE/)
  expect(requests().at(-1)?.searchParams.get('actor')).toBe('ALICE')
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(0)
  await expect(page.getByRole('cell', { name: '192.0.2.99', exact: true })).toHaveCount(0)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Who: ALICE')
})

test('the account picked in the sheet narrows the journal to its entries and names it in a chip', async ({ page }) => {
  const requests = await openJournal(page)

  await page.goto('/settings/audit')
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'bob' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
  expect(requests().at(-1)?.searchParams.get('account')).toBe(BOB.id)
  await expect(page.getByRole('cell', { name: '192.0.2.99', exact: true })).toBeVisible()
  // Alice's entry is attributed to another account; the administrator's carries none.
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toHaveCount(0)
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(0)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')
})

test('removing the account chip widens the journal again', async ({ page }) => {
  await openJournal(page)

  await page.goto(`/settings/audit?account=${ALICE.id}`)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: alice')
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(0)

  await page.getByRole('button', { name: 'Remove filter: Account: alice' }).click()

  await expect(page).not.toHaveURL(/account=/)
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()
})

test('a hand-edited link with an actor that is too long or an account that is no id is not honoured', async ({
  page,
}) => {
  const requests = await openJournal(page)

  await page.goto(`/settings/audit?actor=${'a'.repeat(65)}&account=not-an-id`)

  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()
  const sent = requests().at(-1)
  expect(sent?.searchParams.has('actor')).toBe(false)
  expect(sent?.searchParams.has('account')).toBe(false)
})
