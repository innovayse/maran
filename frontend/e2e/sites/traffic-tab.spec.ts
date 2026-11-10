import { expect, test } from '../fixtures/guarded-test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  DEFAULT_SITE_CHOICES,
  stubPhpVersions,
  stubSiteChoices,
  stubSiteDetail,
  stubSites,
} from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { SiteChoices, SiteDetail } from '../../src/types/site'
import type { SiteTraffic } from '../../src/types/siteTraffic'
import type { Page } from '@playwright/test'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const SITE: SiteDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: [],
  backendType: 'static',
  backendTypeName: 'Static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 7,
}

const CHOICES: SiteChoices = {
  ...DEFAULT_SITE_CHOICES,
  trafficWindows: [
    { value: '7', label: 'Last 7 days' },
    { value: '30', label: 'Last 30 days' },
  ],
  logRetentions: [
    { value: '3', label: '3 days' },
    { value: '7', label: '7 days' },
    { value: '14', label: '14 days' },
  ],
}

/** The hostile path: it must reach the page as text and never as an element. */
const HOSTILE_PATH = '/<img src=x onerror="window.__pwned = true">'

const POPULATED: SiteTraffic = {
  days: 7,
  from: '2026-11-01',
  to: '2026-11-07',
  hasData: true,
  totals: { requests: 12345, bytes: 5 * 1024 * 1024 },
  perDay: [
    { date: '2026-11-05', requests: 4000, bytes: 2 * 1024 * 1024, statusClasses: [{ label: 'Success (2xx)', requests: 4000 }] },
    { date: '2026-11-06', requests: 8345, bytes: 3 * 1024 * 1024, statusClasses: [{ label: 'Success (2xx)', requests: 8345 }] },
  ],
  statusCodes: [
    { code: 200, requests: 12000 },
    { code: 404, requests: 345 },
  ],
  topPaths: [
    { path: '/blog/hello-world', requests: 9000 },
    { path: HOSTILE_PATH, requests: 3 },
  ],
  incompleteSince: null,
  updatedAt: '2026-11-07T09:00:00Z',
}

const EMPTY: SiteTraffic = {
  days: 7,
  from: '2026-11-01',
  to: '2026-11-07',
  hasData: false,
  totals: { requests: 0, bytes: 0 },
  perDay: [],
  statusCodes: [],
  topPaths: [],
  incompleteSince: null,
  updatedAt: null,
}

/**
 * Installs the shell, the site, the choices and a traffic endpoint that answers by window.
 * @param page The Playwright page whose network the routes are installed on.
 * @param answer Builds the summary for the requested window; called per request.
 * @returns The windows requested so far, in order.
 */
const stubTraffic = async (page: Page, answer: (days: number) => SiteTraffic): Promise<() => number[]> => {
  const requested: number[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  // The clean-console spec below counts every failed read, so the shell's own reads are answered too.
  await stubShellReads(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteChoices(page, CHOICES)
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await page.route('**/api/v1/sites/*/traffic*', async (route) => {
    const days = Number(new URL(route.request().url()).searchParams.get('days'))
    requested.push(days)
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...answer(days), days }),
    })
  })
  return () => {
    return [...requested]
  }
}

/**
 * Opens the site and switches to its Traffic tab.
 * @param page The Playwright page to drive.
 * @param tabName The tab's label in the interface language.
 * @returns Resolves once the tab has been pressed.
 */
const openTraffic = async (page: Page, tabName = 'Traffic'): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: tabName, exact: true }).click()
}

test('a site with no recorded traffic shows an empty state, not an error', async ({ page }) => {
  await stubTraffic(page, () => {
    return EMPTY
  })

  await openTraffic(page)

  await expect(page.getByText('No traffic recorded yet')).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
})

test('a populated window shows the totals, the status codes and the paths, with a clean console', async ({
  page,
}) => {
  const problems: string[] = []
  page.on('pageerror', (error) => {
    problems.push(error.message)
  })
  page.on('console', (message) => {
    if (message.type() === 'error') {
      problems.push(message.text())
    }
  })
  await stubTraffic(page, () => {
    return POPULATED
  })

  await openTraffic(page)

  await expect(page.getByText('12,345', { exact: true })).toBeVisible()
  await expect(page.getByText('5 MiB', { exact: true })).toBeVisible()
  const statusTable = page.getByRole('table', { name: 'Requests by status code' })
  await expect(statusTable.getByRole('cell', { name: '404' })).toBeVisible()
  const pathsTable = page.getByRole('table', { name: 'Most requested paths' })
  await expect(pathsTable.getByRole('cell', { name: '/blog/hello-world' })).toBeVisible()
  expect(problems).toEqual([])
})

// A path is text a visitor chose. Building markup from it would be an XSS hole in the panel.
test('a path carrying markup is rendered as text, never as an element', async ({ page }) => {
  await stubTraffic(page, () => {
    return POPULATED
  })

  await openTraffic(page)

  await expect(page.getByText(HOSTILE_PATH)).toBeVisible()
  await expect(page.locator('table img')).toHaveCount(0)
  const scriptRan = await page.evaluate(() => {
    return '__pwned' in window
  })
  expect(scriptRan).toBe(false)
})

test('the tab starts on the first window the panel lists', async ({ page }) => {
  const requested = await stubTraffic(page, () => {
    return POPULATED
  })

  await openTraffic(page)

  await expect(page.getByText('12,345', { exact: true })).toBeVisible()
  expect(requested()).toEqual([7])
})

test('switching the window asks the panel for that window again', async ({ page }) => {
  const requested = await stubTraffic(page, () => {
    return POPULATED
  })

  await openTraffic(page)
  await expect(page.getByText('12,345', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Last 30 days' }).click()

  await expect.poll(requested).toEqual([7, 30])
})

test('a summary that may be incomplete says since when', async ({ page }) => {
  await stubTraffic(page, () => {
    return { ...POPULATED, incompleteSince: '2026-11-03' }
  })

  await openTraffic(page)

  await expect(
    page.getByText('These figures may be incomplete since 3 Nov 2026', { exact: false }),
  ).toBeVisible()
})

test('a complete summary shows no incompleteness notice', async ({ page }) => {
  await stubTraffic(page, () => {
    return POPULATED
  })

  await openTraffic(page)

  await expect(page.getByText('12,345', { exact: true })).toBeVisible()
  await expect(page.getByText('may be incomplete')).toHaveCount(0)
})

test('a refused window shows the panels own message', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/sites\/[^/]+\/traffic/)
  await stubTraffic(page, () => {
    return POPULATED
  })
  // Installed last, so it wins over the working stub above.
  await page.route('**/api/v1/sites/*/traffic*', async (route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'SiteTrafficWindowInvalid',
        title: 'Invalid period',
        detail: 'That period is not offered.',
      }),
    })
  })

  await openTraffic(page)

  await expect(page.getByText('That period is not offered.')).toBeVisible()
  await expect(page.getByText('No traffic recorded yet')).toHaveCount(0)
})

// The request total as each language groups its thousands: a comma, a no-break space (Russian), a comma.
for (const [locale, tabName, requests] of [
  ['en', 'Traffic', '12,345'],
  ['ru', 'Трафик', '12\u00a0345'],
  ['hy', 'Տրաֆիկ', '12,345'],
] as const) {
  test(`the traffic tab does not scroll horizontally at 390px in ${locale}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale)
    await stubTraffic(page, () => {
      return { ...POPULATED, incompleteSince: '2026-11-03' }
    })

    await openTraffic(page, tabName)
    await expect(page.getByText(requests, { exact: true })).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
