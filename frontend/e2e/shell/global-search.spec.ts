import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { SearchResult } from '../../src/types/search'

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

  await page.getByRole('dialog').getByRole('combobox').fill('acme')
  await page.getByRole('dialog').getByRole('option', { name: /acme/ }).click()

  await expect(page).toHaveURL(/\/accounts\/11111111-1111-4111-8111-111111111111/)
})

test('a failed search leaves the palette usable with a muted note', async ({ page }) => {
  await page.route('**/api/v1/search', async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{}' })
  })
  await openPalette(page)

  await page.getByRole('dialog').getByRole('combobox').fill('acme')

  await expect(page.getByRole('dialog').getByText('Could not search the panel right now.')).toBeVisible()
})
