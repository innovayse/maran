import { expect, test, type Page } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures/measure-horizontal-extent'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  DEFAULT_SITE_CHOICES,
  stubPhpVersions,
  stubSiteChoices,
  stubSiteDetail,
  stubSites,
} from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { Site, SiteChoices, SiteDetail } from '../../src/types/site'

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

/** What the panel answers a retention change with: the site, list-shaped. */
const SITE_ROW: Site = {
  id: SITE.id,
  accountId: SITE.accountId,
  ownerName: 'alpha',
  domain: SITE.domain,
  backendType: 'static',
  backendTypeName: 'Static',
  phpVersion: '',
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: SITE.createdAt,
  logRetentionDays: 3,
}

const CHOICES: SiteChoices = {
  ...DEFAULT_SITE_CHOICES,
  trafficWindows: [{ value: '7', label: 'Last 7 days' }],
  logRetentions: [
    { value: '3', label: '3 days' },
    { value: '7', label: '7 days' },
    { value: '14', label: '14 days' },
  ],
}

/**
 * Installs the shell, the site and the choices, and a retention endpoint that records its bodies.
 * @param page The Playwright page whose network the routes are installed on.
 * @param refusal The panel's refusal to answer every change with, or `null` to accept them.
 * @returns The request bodies the retention endpoint received, in order.
 */
const stubRetention = async (page: Page, refusal: string | null): Promise<() => unknown[]> => {
  const bodies: unknown[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteChoices(page, CHOICES)
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await page.route('**/api/v1/sites/*/log-retention', async (route) => {
    bodies.push(route.request().postDataJSON())
    if (refusal !== null) {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SiteLogRetentionInvalid', title: 'Invalid retention', detail: refusal }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(SITE_ROW) })
  })
  return () => {
    return [...bodies]
  }
}

/**
 * Opens the site's Logs tab.
 * @param page The Playwright page to drive.
 * @returns Resolves once the tab has been pressed.
 */
const openLogs = async (page: Page): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Logs', exact: true }).click()
}

/**
 * Picks a retention in the select.
 * @param page The Playwright page to drive.
 * @param label The option's label.
 * @returns Resolves once the option has been chosen.
 */
const choose = async (page: Page, label: string): Promise<void> => {
  await page.getByRole('combobox', { name: 'Keep logs for' }).click()
  await page.getByRole('option', { name: label }).click()
}

test('the logs tab shows the retention the site keeps now', async ({ page }) => {
  await stubRetention(page, null)

  await openLogs(page)

  await expect(page.getByRole('combobox', { name: 'Keep logs for' })).toContainText('7 days')
  // Nothing to save until the pick differs from what the site keeps.
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
})

test('changing the retention sends the new number of days and says it was saved', async ({ page }) => {
  const bodies = await stubRetention(page, null)

  await openLogs(page)
  await choose(page, '3 days')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByText('The log retention has been saved.')).toBeVisible()
  expect(bodies()).toEqual([{ days: 3 }])
  await expect(page.getByRole('combobox', { name: 'Keep logs for' })).toContainText('3 days')
})

test('a refused retention shows the panels own message and keeps the pick', async ({ page }) => {
  await stubRetention(page, 'Logs can be kept for 1 to 14 days.')

  await openLogs(page)
  await choose(page, '14 days')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByText('Logs can be kept for 1 to 14 days.')).toBeVisible()
  await expect(page.getByText('The log retention has been saved.')).toHaveCount(0)
})

for (const [locale, logsTab] of [
  ['en', 'Logs'],
  ['ru', 'Логи'],
  ['hy', 'Լոգեր'],
] as const) {
  test(`the retention control does not scroll horizontally at 390px in ${locale}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await setPersistedLocale(page, locale)
    await stubRetention(page, 'Logs can be kept for 1 to 14 days.')

    await page.goto(`/sites/${SITE.id}`)
    await page.getByRole('button', { name: logsTab, exact: true }).click()
    await expect(page.getByRole('main').getByRole('combobox').first()).toBeVisible()

    await expectNoHorizontalOverflow(page)
  })
}
