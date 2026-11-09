import { expect, test, type Page } from '@playwright/test'
import { mainNavigation } from '../fixtures/open-full-shell'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubResellerAudit } from '../fixtures/stub-audit-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubResellerSession } from '../fixtures/stub-reseller-session'
import type { Account } from '../../src/types/account'
import type { AuditEvent } from '../../src/types/audit'

// Issue #370: a reseller reads its own part of the audit journal, the entries about its accounts and
// its own login. The panel decides which entries those are (the backend tests hold that); what is
// asserted here is that the reseller reaches the screen by clicks, that every read goes to the
// reseller's endpoint and never to an administrator's, that search, sort and filters are the
// panel's, and that the administrator's export and upkeep cards are not drawn.

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

const OWN_LOGIN: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e1',
  occurredAt: '2026-09-10T09:00:00+00:00',
  actorUsername: 'acmehosting',
  impersonatorUsername: null,
  action: 'LoginSucceeded',
  actionName: 'Signed in',
  subject: 'acmehosting',
  ipAddress: '203.0.113.7',
  succeeded: true,
  outcomeName: 'Succeeded',
}

const ALICE_REFUSED: AuditEvent = {
  id: '00000000-0000-0000-0000-0000000000e2',
  occurredAt: '2026-09-09T08:00:00+00:00',
  actorUsername: 'alice',
  impersonatorUsername: null,
  action: 'LoginFailed',
  actionName: 'Sign-in refused',
  subject: 'alice',
  ipAddress: '198.51.100.4',
  succeeded: false,
  outcomeName: 'Failed',
}

const BOB_SIGN_IN: AuditEvent = {
  ...OWN_LOGIN,
  id: '00000000-0000-0000-0000-0000000000e3',
  occurredAt: '2026-09-08T08:00:00+00:00',
  actorUsername: 'bob',
  subject: 'bob',
  ipAddress: '192.0.2.99',
}

/** Which account the panel attributed each entry to; the reseller's own sign-in has none. */
const ACCOUNT_OF: Record<string, string> = {
  [ALICE_REFUSED.id]: ALICE.id,
  [BOB_SIGN_IN.id]: BOB.id,
}

/**
 * Signs a reseller in over its part of the journal and records every request to an administrator's
 * audit endpoint, which a reseller's screen must never make.
 * @param page The Playwright page.
 * @returns The reseller list requests, and the administrator endpoint requests (expected empty).
 */
const openJournal = async (
  page: Page,
): Promise<{ list: () => readonly URL[]; administrator: () => readonly string[] }> => {
  const administratorReads: string[] = []
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname
    if (/^\/api\/v1\/audit(\/|$)/.test(path) && !path.startsWith('/api/v1/audit/reseller')) {
      administratorReads.push(path)
    }
  })
  await stubResellerSession(page)
  await stubAccounts(page, [ALICE, BOB])
  await stubAccountNames(page, [ALICE, BOB])
  const list = await stubResellerAudit(page, [OWN_LOGIN, ALICE_REFUSED, BOB_SIGN_IN], ACCOUNT_OF)
  return {
    list,
    administrator: (): readonly string[] => {
      return administratorReads
    },
  }
}

/**
 * Clicks the reseller's way to its journal. The Activity section holds that one page for a reseller,
 * and a section with a single page is drawn as that page under the section's name, so the link is
 * named "Activity" and leads to the journal.
 * @param page The Playwright page.
 * @returns Resolves once the journal's address is open.
 */
const openJournalFromSidebar = async (page: Page): Promise<void> => {
  await mainNavigation(page).getByRole('link', { name: 'Activity', exact: true }).click()
  await expect(page).toHaveURL('/reseller/audit')
}

test('a reseller reaches its journal from the sidebar and reads only the reseller endpoint', async ({ page }) => {
  const { list, administrator } = await openJournal(page)

  await page.goto('/')
  await openJournalFromSidebar(page)

  await expect(page).toHaveURL('/reseller/audit')
  await expect(page.getByRole('heading', { level: 1, name: 'Audit journal' })).toBeVisible()
  await expect(page.getByText('The entries about your accounts and your own sign-ins')).toBeVisible()
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toBeVisible()
  expect(list().length).toBeGreaterThan(0)
  // The administrator's upkeep and export are not the reseller's.
  await expect(page.getByRole('button', { name: /Export/ })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: /retention|integrity/i })).toHaveCount(0)
  expect(administrator()).toEqual([])
})

test('the search and a column sort reach the reseller endpoint', async ({ page }) => {
  const { list } = await openJournal(page)

  await page.goto('/')
  await openJournalFromSidebar(page)
  await page.getByRole('searchbox', { name: 'Search the journal' }).fill('alice')

  await expect(page).toHaveURL(/search=alice/)
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: '203.0.113.7', exact: true })).toHaveCount(0)
  expect(list().at(-1)?.searchParams.get('search')).toBe('alice')

  await page.getByRole('searchbox', { name: 'Search the journal' }).fill('')
  const heading = page.getByRole('columnheader', { name: 'Who' })
  await heading.getByRole('button').click()
  await expect(page).toHaveURL(/sort=actor/)
  expect(list().at(-1)?.searchParams.get('sort')).toBe('actor')
})

test('the account picked in the sheet narrows the reseller journal and names it in a chip', async ({ page }) => {
  const { list, administrator } = await openJournal(page)

  await page.goto('/')
  await openJournalFromSidebar(page)
  await page.getByTestId('filter-trigger').click()
  const dialog = page.getByRole('dialog', { name: 'Filters' })
  await dialog.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: 'bob' }).click()
  await dialog.getByRole('combobox', { name: 'Outcome' }).click()
  await page.getByRole('option', { name: 'Succeeded' }).click()
  await dialog.getByRole('button', { name: 'Apply' }).click()

  await expect(page).toHaveURL(new RegExp(`account=${BOB.id}`))
  const sent = list().at(-1)
  expect(sent?.searchParams.get('account')).toBe(BOB.id)
  expect(sent?.searchParams.get('outcome')).toBe('succeeded')
  await expect(page.getByRole('cell', { name: '192.0.2.99', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: '198.51.100.4', exact: true })).toHaveCount(0)
  await expect(page.getByTestId('active-filter-chips')).toContainText('Account: bob')
  expect(administrator()).toEqual([])
})

test('an account the panel does not know to the reseller is shown as the panel refuses it', async ({ page }) => {
  await openJournal(page)
  await page.route(/\/api\/v1\/audit\/reseller\?.*account=/, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AuditAccountNotFound', title: 'Not found', detail: 'There is no such account in your audit journal.' }),
    })
  })

  await page.goto(`/reseller/audit?account=${BOB.id}`)

  await expect(page.getByText('There is no such account in your audit journal.')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('an administrator opening the reseller journal address lands on the not-available page', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
  await stubResellerAudit(page, [OWN_LOGIN])

  await page.goto('/reseller/audit')

  await expect(page).toHaveURL('/not-available')
  await expect(page.getByRole('heading', { level: 1, name: 'Page not available' })).toBeVisible()
})
