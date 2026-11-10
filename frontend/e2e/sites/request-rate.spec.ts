import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  EMPTY_SITE_OPTIONS,
  OPTIONS_SITE_ID,
  stubSiteOptions,
  stubSiteOptionsRefusal,
  type SiteOptionsStub,
} from '../fixtures/stub-site-options-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'
import type { SiteOptions } from '../../src/types/siteOptions'
import type { Page } from '@playwright/test'

// The request limit on a site's Options tab (issue #159): how many requests per second one visitor
// address may make, and a burst on top. An empty rate is "no limit". The bounds come from the panel.

const LICENSED_SITES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const SITE: SiteDetail = {
  id: OPTIONS_SITE_ID,
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'example.com',
  aliases: ['www.example.com'],
  backendType: 'php',
  backendTypeName: 'PHP',
  phpVersion: '8.3',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/example.com/public',
  hasCertificate: false,
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

/** A site that is already limited to ten requests a second with a burst of twenty. */
const LIMITED: SiteOptions = { ...EMPTY_SITE_OPTIONS, requestRate: { perSecond: 10, burst: 20 } }

/**
 * Installs the shell, the site and its options.
 * @param page The Playwright page whose network the routes are installed on.
 * @param options The options the panel starts with.
 * @returns What the page sends to the options endpoint.
 */
const stubOptionsTab = async (page: Page, options: SiteOptions = EMPTY_SITE_OPTIONS): Promise<SiteOptionsStub> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  return stubSiteOptions(page, options)
}

/**
 * Opens the site's Options tab.
 * @param page The Playwright page to drive.
 * @returns Resolves once the tab is chosen.
 */
const openOptions = async (page: Page): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Options' }).click()
}

test('the request limit shows what the panel holds', async ({ page }) => {
  await stubOptionsTab(page, LIMITED)

  await openOptions(page)

  await expect(page.getByLabel('Requests per second, per visitor')).toHaveValue('10')
  await expect(page.getByLabel('Extra requests allowed at once')).toHaveValue('20')
})

test('a site with no limit shows both fields empty', async ({ page }) => {
  await stubOptionsTab(page)

  await openOptions(page)

  await expect(page.getByLabel('Requests per second, per visitor')).toHaveValue('')
  await expect(page.getByLabel('Extra requests allowed at once')).toHaveValue('')
})

test('the bounds in the hints come from the panel, not from the page', async ({ page }) => {
  await stubOptionsTab(page, {
    ...EMPTY_SITE_OPTIONS,
    limits: { ...EMPTY_SITE_OPTIONS.limits, maximumRequestsPerSecond: 250, maximumRequestBurst: 40 },
  })

  await openOptions(page)

  await expect(page.getByText(/From 1 to 250; leave empty for no limit/)).toBeVisible()
  await expect(page.getByText(/From 0 to 40; leave empty for none/)).toBeVisible()
})

test('a rate and a burst are sent as one limit', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByLabel('Requests per second, per visitor').fill('5')
  await page.getByLabel('Extra requests allowed at once').fill('15')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved[0].requestRate).toEqual({ perSecond: 5, burst: 15 })
})

test('a rate with an empty burst is sent with a burst of none', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByLabel('Requests per second, per visitor').fill('5')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved[0].requestRate).toEqual({ perSecond: 5, burst: 0 })
})

test('clearing the rate removes the limit', async ({ page }) => {
  const stub = await stubOptionsTab(page, LIMITED)
  await openOptions(page)

  await page.getByLabel('Requests per second, per visitor').fill('')
  await page.getByLabel('Extra requests allowed at once').fill('')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved[0].requestRate).toBeNull()
})

test('a burst without a rate is not sent and is explained', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByLabel('Extra requests allowed at once').fill('15')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('Enter a requests-per-second limit too, or clear the burst.')).toBeVisible()
  expect(stub.saved).toHaveLength(0)
})

test('a rate that is not a whole number is not sent and is explained', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByLabel('Requests per second, per visitor').fill('fast')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('Enter a whole number, or leave the field empty.')).toBeVisible()
  expect(stub.saved).toHaveLength(0)
})

test('the panel refusal of a rate is shown under the rate with its own words', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/sites\/[^/]+\/options$/)
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubSiteOptionsRefusal(page, 'SiteRequestRateOutOfRange', 'That request limit is outside what a site may set.')
  await openOptions(page)

  await page.getByLabel('Requests per second, per visitor').fill('5000')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('That request limit is outside what a site may set.')).toBeVisible()
  // The refusal keeps what was typed, so the operator fixes one field rather than retyping the form.
  await expect(page.getByLabel('Requests per second, per visitor')).toHaveValue('5000')
  await expect(page.getByText('The options were saved.')).toHaveCount(0)
})
