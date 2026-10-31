import { expect, test, type Page } from '@playwright/test'
import { breadcrumbItems, mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { delayRoute } from '../fixtures/delay-route'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { Account, AccountDetail } from '../../src/types/account'
import type { Site, SiteDetail } from '../../src/types/site'

// Issue #209, point 6: the breadcrumb stopped one level early on submenu pages and on records, and
// a record's crumb must be its name (a placeholder while it loads), never the id in the address.
// Issue #72 is the same mechanism: a settings screen's crumb was its URL segment.

const SITE: Site = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  ownerName: 'alice',
  domain: 'alpha.example.com',
  backendType: 'static',
  phpVersion: '',
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

const SITE_DETAIL: SiteDetail = {
  id: SITE.id,
  accountId: SITE.accountId,
  domain: SITE.domain,
  aliases: [],
  backendType: 'static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alice/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: SITE.createdAt,
}

const ACCOUNT: Account = {
  id: '00000000-0000-0000-0000-0000000000d1',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '00000000-0000-0000-0000-0000000000a1',
  status: 'active',
  createdAt: '2026-08-30T09:00:00+00:00',
}

const ACCOUNT_DETAIL: AccountDetail = { ...ACCOUNT, planDisplayName: 'Starter', ownerEmail: 'alice@example.com' }

/**
 * Reads the trail as one string, crumbs joined the way they are drawn.
 * @param page The Playwright page under test.
 * @returns The trail.
 */
const trail = async (page: Page): Promise<string> => {
  const crumbs = await breadcrumbItems(page).allInnerTexts()
  return crumbs
    .map((crumb) => {
      return crumb.trim()
    })
    .join(' / ')
}

/**
 * Waits for the trail to read exactly as expected.
 * @param page The Playwright page under test.
 * @param expected The whole trail, crumbs joined by " / ".
 * @returns Resolves once it does.
 */
const expectTrail = async (page: Page, expected: string): Promise<void> => {
  await expect
    .poll(() => {
      return trail(page)
    })
    .toBe(expected)
}

/** One submenu page and the trail it must show. */
interface SubmenuCase {
  /** The section's name. */
  section: string
  /** The child to click. */
  child: string
  /** The whole trail. */
  expected: string
}

const SUBMENU_CASES: SubmenuCase[] = [
  { section: 'Backups', child: 'Schedule', expected: 'Security / Backups / Schedule' },
  { section: 'Backups', child: 'Destinations', expected: 'Security / Backups / Destinations' },
  { section: 'Sites', child: 'New site', expected: 'Hosting / Sites / New site' },
  { section: 'Databases', child: 'Grant repair', expected: 'Hosting / Databases / Grant repair' },
  { section: 'Accounts', child: 'New account', expected: 'Hosting / Accounts / New account' },
  { section: 'Firewall', child: 'Bans', expected: 'Security / Firewall / Bans' },
  { section: 'Firewall', child: 'Never banned', expected: 'Security / Firewall / Never banned' },
]

for (const { section, child, expected } of SUBMENU_CASES) {
  test(`${section} → ${child} ends the trail on the page itself`, async ({ page }) => {
    await openFullShell(page)
    await openSection(page, section)
    await mainNavigation(page).getByRole('link', { name: child, exact: true }).click()
    await expectTrail(page, expected)
  })
}

test('a site ends the trail on its domain, with a placeholder while it loads', async ({ page }) => {
  await openFullShell(page)
  await stubSites(page, [SITE])
  await stubSiteDetail(page, SITE_DETAIL)
  await delayRoute(page, `**/api/v1/sites/${SITE.id}`, 1500)

  await mainNavigation(page).getByRole('link', { name: 'Sites', exact: true }).click()
  await page.getByRole('main').getByRole('link', { name: SITE.domain }).click()

  // While the site is on its way the last crumb is a placeholder, and the id never shows.
  await expect(page.getByRole('navigation', { name: 'Breadcrumb' }).locator('[data-ui="crumb-loading"] [data-skeleton]')).toBeVisible()
  expect(await trail(page)).not.toContain(SITE.id)

  await expectTrail(page, `Hosting / Sites / ${SITE.domain}`)
})

test('an account ends the trail on its name', async ({ page }) => {
  await openFullShell(page)
  await stubAccounts(page, [ACCOUNT])
  await page.route(`**/api/v1/accounts/${ACCOUNT.id}`, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ACCOUNT_DETAIL) })
  })

  await mainNavigation(page).getByRole('link', { name: 'Accounts', exact: true }).click()
  await page.getByRole('main').getByRole('link', { name: ACCOUNT.name, exact: true }).click()
  await expect(page).toHaveURL(`/accounts/${ACCOUNT.id}`)

  await expectTrail(page, `Hosting / Accounts / ${ACCOUNT.name}`)
})

test('an account the panel does not have ends the trail on the screen title, not on its id', async ({
  page,
}) => {
  await openFullShell(page, `/accounts/${ACCOUNT.id}`)
  await expect(page.getByRole('main')).toBeVisible()
  await expectTrail(page, 'Hosting / Accounts / Account')
})

test('settings screens are named by their titles, never by a URL segment (#72)', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  await nav.getByRole('link', { name: 'Audit journal', exact: true }).click()
  await expectTrail(page, 'System / Audit journal')

  await nav.getByRole('link', { name: 'Security policy', exact: true }).click()
  await expectTrail(page, 'System / Security policy')

  await nav.getByRole('link', { name: 'Signed-in devices', exact: true }).click()
  await expectTrail(page, 'System / Signed-in devices')

  await openSection(page, 'Sites')
  await nav.getByRole('link', { name: 'PHP versions', exact: true }).click()
  await expectTrail(page, 'Hosting / Sites / PHP versions')
})
