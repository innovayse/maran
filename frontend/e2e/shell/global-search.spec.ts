import { expect, test } from '../fixtures/guarded-test'
import { stubAccountResources } from '../fixtures/stub-account-resources-route'
import { stubAccountNames } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubInvitation } from '../fixtures/stub-invitation-routes'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { SearchResult } from '../../src/types/search'
import type { Page } from '@playwright/test'

// Issue #198 slice S1: the jump-to palette shows what the panel's global search finds, grouped, and
// keeps working when a group cannot be answered.

/** One account and one site, the two kinds the first slice finds. */
const ANSWER: SearchResult = {
  groups: [
    {
      kind: 'account',
      state: 'ready',
      hits: [{ kind: 'account', id: '11111111-1111-4111-8111-111111111111', title: 'acme', subtitle: 'acme.example.com · o@acme.example' }],
    },
    { kind: 'site', state: 'unavailable', hits: [] },
  ],
}

/** The account the found hit leads to, as the panel reports it on its detail screen. */
const ACME: Account = {
  id: '11111111-1111-4111-8111-111111111111',
  name: 'acme',
  primaryDomain: 'acme.example.com',
  planId: '22222222-2222-4222-8222-222222222222',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-30T09:00:00+00:00',
  resellerId: null,
  resellerName: null,
}

/**
 * Stubs the search endpoint and records every body it was sent.
 * @param page The page whose network the stub is installed on.
 * @param answer What the stub answers.
 * @returns The bodies received, in order.
 */
const stubSearch = async (page: Page, answer: SearchResult): Promise<string[]> => {
  const bodies: string[] = []
  await page.route('**/api/v1/search', async (route) => {
    bodies.push(route.request().postData() ?? '')
    expect(route.request().method()).toBe('POST')
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) })
  })
  return bodies
}

/**
 * Opens the palette on a signed-in panel.
 * @param page The page to drive.
 * @returns Resolves once the palette is on screen.
 */
const openPalette = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  // Accounts is licensed: a hit leads into its screen, and an unlicensed module's route is the upgrade page.
  await stubModules(page, [
    { name: 'accounts', displayName: 'Accounts', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])
  await page.goto('/')
  await page.getByRole('button', { name: 'Search or jump to…' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
}

test('typing two characters shows the server groups, and an unavailable group is a muted note', async ({ page }) => {
  const bodies = await stubSearch(page, ANSWER)
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('acme')

  await expect(page.getByRole('dialog').getByRole('option', { name: /acme/ })).toBeVisible()
  await expect(page.getByRole('dialog').getByText('Could not search Sites right now.')).toBeVisible()
  expect(bodies).toEqual([JSON.stringify({ term: 'acme' })])
})

test('one character is never sent to the server', async ({ page }) => {
  const bodies = await stubSearch(page, ANSWER)
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('a')
  await page.waitForTimeout(500)

  expect(bodies).toEqual([])
})

test('choosing a found account opens its detail screen', async ({ page }) => {
  await stubSearch(page, ANSWER)
  await openPalette(page)
  // The screen the hit opens reads the account, its use of the plan and its invitation.
  await stubAccountNames(page, [ACME])
  await stubAccountResources(page, { accountId: ACME.id, resources: [] })
  await stubInvitation(page, ACME.id, 'pending')

  await page.getByRole('dialog').getByRole('combobox').fill('acme')
  await page.getByRole('dialog').getByRole('option', { name: /acme/ }).click()

  await expect(page).toHaveURL(/\/accounts\/11111111-1111-4111-8111-111111111111/)
})

test('a failed search leaves the palette usable with a muted note', async ({ page, guard }) => {
  // The refusal is the subject: the palette shows a note instead of hits.
  guard.allow(500, /\/api\/v1\/search$/)
  await page.route('**/api/v1/search', async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{}' })
  })
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('acme')

  await expect(page.getByRole('dialog').getByText('Could not search the panel right now.')).toBeVisible()
})
