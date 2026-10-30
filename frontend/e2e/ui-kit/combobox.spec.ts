import { expect, test, type Page, type Request } from '@playwright/test'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubDatabases } from '../fixtures/stub-databases-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { Account } from '../../src/types/account'

// Issue #196: account pickers search the panel instead of offering the first hundred accounts held
// in the browser. `UiCombobox` is exercised on the database create form, the simplest screen that
// draws one: the server narrows, the keyboard chooses, Escape gives focus back, the popup says how
// many matches it leaves out, and a chosen name stays on the trigger after the page of matches no
// longer holds it.

/** Thirty accounts, five more than one picker page, named so their order is their number. */
const ACCOUNTS: Account[] = Array.from({ length: 30 }, (_, index): Account => {
  const number = String(index + 1).padStart(2, '0')
  return {
    id: `00000000-0000-0000-0000-0000000000${number}`,
    name: `acct-${number}`,
    primaryDomain: `acct-${number}.example.com`,
    planId: '44444444-4444-4444-4444-444444444444',
    status: 'active',
    createdAt: '2026-08-01T10:00:00Z',
  }
})

/**
 * Whether a request is a read of the account choices.
 * @param request The request the page sent.
 * @returns True for `GET /api/v1/accounts/choices`.
 */
const isChoicesRead = (request: Request): boolean => {
  return new URL(request.url()).pathname === '/api/v1/accounts/choices'
}

/**
 * Opens the databases screen as an administrator over the thirty-account catalogue.
 * @param page The Playwright page.
 * @returns Every choices read so far, oldest first.
 */
const openScreen = async (page: Page): Promise<() => readonly URL[]> => {
  const reads: URL[] = []
  page.on('request', (request) => {
    if (isChoicesRead(request)) {
      reads.push(new URL(request.url()))
    }
  })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true }])
  await stubAccounts(page, ACCOUNTS)
  await stubDatabases(page, [])
  await page.goto('/databases')
  return () => {
    return reads
  }
}

test('opening asks the panel for the first page and says how many more match', async ({ page }) => {
  const reads = await openScreen(page)

  await page.getByRole('combobox', { name: 'Account' }).click()

  await expect(page.getByRole('option')).toHaveCount(25)
  await expect(page.getByText('Type to narrow — 5 more')).toBeVisible()
  // An empty search is left out of the request, and the page size is the picker's own.
  expect(reads()[0]?.searchParams.has('search')).toBe(false)
  expect(reads()[0]?.searchParams.get('pageSize')).toBe('25')
})

test('typing narrows through the server, not by filtering what the browser holds', async ({ page }) => {
  const reads = await openScreen(page)
  await page.getByRole('combobox', { name: 'Account' }).click()
  await expect(page.getByRole('option')).toHaveCount(25)

  const narrowed = page.waitForRequest((request) => {
    return isChoicesRead(request) && new URL(request.url()).searchParams.get('search') === 'acct-28'
  })
  await page.getByRole('searchbox', { name: 'Search' }).fill('acct-28')
  await narrowed

  // acct-28 was never on the first page, so only the server's answer can put it here.
  await expect(page.getByRole('option')).toHaveText(['acct-28'])
  await expect(page.getByText(/Type to narrow/)).toHaveCount(0)
  expect(reads().length).toBeGreaterThan(1)
})

test('the keyboard opens the list, moves through it and chooses', async ({ page }) => {
  await openScreen(page)
  const picker = page.getByRole('combobox', { name: 'Account' })

  await picker.focus()
  await page.keyboard.press('Enter')
  const search = page.getByRole('searchbox', { name: 'Search' })
  await expect(search).toBeFocused()
  await expect(page.getByRole('option')).toHaveCount(25)

  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('Enter')

  await expect(page.getByRole('listbox')).toHaveCount(0)
  await expect(picker).toHaveText('acct-02')
  await expect(picker).toBeFocused()
})

test('Escape closes the list without choosing and returns focus to the picker', async ({ page }) => {
  await openScreen(page)
  const picker = page.getByRole('combobox', { name: 'Account' })

  await picker.click()
  await expect(page.getByRole('option')).toHaveCount(25)
  await page.keyboard.press('Escape')

  await expect(page.getByRole('listbox')).toHaveCount(0)
  await expect(picker).toBeFocused()
  await expect(picker).toHaveText('Choose an account')
})

test('a chosen account stays named after a search whose page does not hold it', async ({ page }) => {
  await openScreen(page)
  const picker = page.getByRole('combobox', { name: 'Account' })

  await picker.click()
  await page.getByRole('searchbox', { name: 'Search' }).fill('acct-29')
  await page.getByRole('option', { name: 'acct-29' }).click()
  await expect(picker).toHaveText('acct-29')

  // Reopened, the picker asks for the first page again, which does not hold acct-29.
  await picker.click()
  await expect(page.getByRole('option')).toHaveCount(25)
  await expect(page.getByRole('option', { name: 'acct-29' })).toHaveCount(0)
  await page.keyboard.press('Escape')

  await expect(picker).toHaveText('acct-29')
})

test('an answer to an older search arriving late does not replace the newer one', async ({ page }) => {
  await openScreen(page)
  let releaseSlow: () => void = () => {}
  const slowHeld = new Promise<void>((resolve) => {
    releaseSlow = resolve
  })
  // Installed after the catalogue route, so it is asked first; every other search falls through.
  await page.route(/\/api\/v1\/accounts\/choices\?.*search=acct-1(&|$)/, async (route) => {
    await slowHeld
    await route.fallback()
  })

  await page.getByRole('combobox', { name: 'Account' }).click()
  const search = page.getByRole('searchbox', { name: 'Search' })
  const slowSent = page.waitForRequest((request) => {
    return isChoicesRead(request) && new URL(request.url()).searchParams.get('search') === 'acct-1'
  })
  await search.fill('acct-1')
  await slowSent
  await search.fill('acct-21')
  await expect(page.getByRole('option')).toHaveText(['acct-21'])

  const slowAnswered = page.waitForResponse((response) => {
    return isChoicesRead(response.request()) && new URL(response.url()).searchParams.get('search') === 'acct-1'
  })
  releaseSlow()
  // Wait for the held answer to be delivered, so the assertion below is made after it could have
  // replaced the list — before that point it would pass whatever the store did with it.
  await slowAnswered
  await expect(page.getByRole('option')).toHaveText(['acct-21'])
})
