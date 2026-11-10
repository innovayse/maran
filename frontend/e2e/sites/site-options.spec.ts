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

// A site's Options tab (issue #151): redirects, password protection, IP rules, upload size and cache
// lifetime, saved as one set. Every list the selects offer and every bound comes from the panel's
// answer, so the specs answer with them and assert the page shows exactly those.

const LICENSED_SITES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

const SITE: SiteDetail = {
  id: OPTIONS_SITE_ID,
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'example.com',
  aliases: ['www.example.com'],
  backendType: 'static',
  backendTypeName: 'Static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/example.com/public',
  hasCertificate: false,
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

/** A site that already has one of everything, with a user whose password the panel keeps. */
const CONFIGURED: SiteOptions = {
  ...EMPTY_SITE_OPTIONS,
  canonicalHost: 'www',
  clientMaxBodySizeMib: 64,
  staticCacheDays: 30,
  redirects: [{ path: '/old', match: 'exact', target: '/new', status: 'permanent' }],
  basicAuthUsers: ['alice'],
  ipAccessMode: 'allowList',
  ipRules: ['203.0.113.0/24'],
}

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

test('the Options tab shows what the panel holds, filled in', async ({ page }) => {
  await stubOptionsTab(page, CONFIGURED)

  await openOptions(page)

  await expect(page.getByRole('combobox', { name: 'Main name' })).toContainText('Use the www name')
  await expect(page.getByLabel('Largest upload (MiB)')).toHaveValue('64')
  await expect(page.getByLabel('Browser cache for static files (days)')).toHaveValue('30')
  await expect(page.getByLabel('Path of rule 1')).toHaveValue('/old')
  await expect(page.getByLabel('Send to, rule 1')).toHaveValue('/new')
  await expect(page.getByLabel('User name 1')).toHaveValue('alice')
  await expect(page.getByLabel('Addresses')).toHaveValue('203.0.113.0/24')
})

test('a site with nothing set shows empty sections and says so', async ({ page }) => {
  await stubOptionsTab(page)

  await openOptions(page)

  await expect(page.getByText('This site has no redirects.')).toBeVisible()
  await expect(page.getByText('This site is open to everyone.')).toBeVisible()
  await expect(page.getByLabel('Largest upload (MiB)')).toHaveValue('')
})

test('the bounds in the hints come from the panel, not from the page', async ({ page }) => {
  await stubOptionsTab(page, {
    ...EMPTY_SITE_OPTIONS,
    limits: { ...EMPTY_SITE_OPTIONS.limits, maximumBodySizeMib: 512, maximumCacheDays: 90 },
  })

  await openOptions(page)

  await expect(page.getByText(/From 1 to 512 MiB/)).toBeVisible()
  await expect(page.getByText(/From 1 to 90 days/)).toBeVisible()
})

test('the password of an existing user is never shown and is not sent when left empty', async ({ page }) => {
  const stub = await stubOptionsTab(page, CONFIGURED)
  await openOptions(page)

  await expect(page.getByLabel('Password for user 1')).toHaveValue('')
  await expect(page.getByLabel('Password for user 1')).toHaveAttribute(
    'placeholder',
    'Leave empty to keep the current password',
  )
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved).toHaveLength(1)
  expect(stub.saved[0].basicAuthUsers).toEqual([{ name: 'alice', password: null }])
})

test('adding a redirect and saving sends the whole set the page shows', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByRole('button', { name: 'Add a redirect' }).click()
  await page.getByLabel('Path of rule 1').fill('/blog/')
  await page.getByRole('combobox', { name: 'Applies to, rule 1' }).click()
  await page.getByRole('option', { name: 'This folder and everything in it' }).click()
  await page.getByLabel('Send to, rule 1').fill('https://example.org/blog/')
  await page.getByRole('combobox', { name: 'Kind of redirect, rule 1' }).click()
  await page.getByRole('option', { name: 'Temporary (302)' }).click()
  await page.getByLabel('Largest upload (MiB)').fill('128')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved).toEqual([
    {
      canonicalHost: 'none',
      clientMaxBodySizeMib: 128,
      staticCacheDays: null,
      redirects: [{ path: '/blog/', match: 'prefix', target: 'https://example.org/blog/', status: 'temporary' }],
      basicAuthUsers: [],
      ipAccessMode: null,
      ipRules: [],
      requestRate: null,
    },
  ])
})

test('a removed redirect is gone from what is sent', async ({ page }) => {
  const stub = await stubOptionsTab(page, CONFIGURED)
  await openOptions(page)

  await page.getByRole('button', { name: 'Remove rule 1' }).click()
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved[0].redirects).toEqual([])
})

test('the add button stops at the limit the panel states', async ({ page }) => {
  await stubOptionsTab(page, {
    ...EMPTY_SITE_OPTIONS,
    limits: { ...EMPTY_SITE_OPTIONS.limits, maximumRedirects: 1 },
    redirects: [{ path: '/a', match: 'exact', target: '/b', status: 'permanent' }],
  })

  await openOptions(page)

  await expect(page.getByRole('button', { name: 'Add a redirect' })).toBeDisabled()
  await expect(page.getByText('1 of 1 rules')).toBeVisible()
})

test('turning on password protection asks first, and cancelling sends nothing', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByRole('button', { name: 'Add a user' }).click()
  await page.getByLabel('User name 1').fill('bob')
  await page.getByLabel('Password for user 1').fill('a long enough password')
  await page.getByRole('button', { name: 'Save options' }).click()

  const dialog = page.getByRole('dialog', { name: 'Turn on password protection' })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText('every visitor')
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await expect(dialog).toBeHidden()
  expect(stub.saved).toHaveLength(0)
})

test('confirming password protection sends the new user with its password, once, and empties the field', async ({
  page,
}) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByRole('button', { name: 'Add a user' }).click()
  await page.getByLabel('User name 1').fill('bob')
  await page.getByLabel('Password for user 1').fill('a long enough password')
  await page.getByRole('button', { name: 'Save options' }).click()
  await page.getByRole('dialog', { name: 'Turn on password protection' }).getByRole('button', { name: 'Turn on protection' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  expect(stub.saved[0].basicAuthUsers).toEqual([{ name: 'bob', password: 'a long enough password' }])
  // After the save the user exists and the panel has returned names only, so the field is empty again.
  await expect(page.getByLabel('Password for user 1')).toHaveValue('')
  await expect(page.getByLabel('User name 1')).toBeDisabled()
})

test('a site that already has users saves a new one without asking again', async ({ page }) => {
  const stub = await stubOptionsTab(page, CONFIGURED)
  await openOptions(page)

  await page.getByRole('button', { name: 'Add a user' }).click()
  await page.getByLabel('User name 2').fill('carol')
  await page.getByLabel('Password for user 2').fill('another long password')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('The options were saved.')).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(stub.saved[0].basicAuthUsers).toEqual([
    { name: 'alice', password: null },
    { name: 'carol', password: 'another long password' },
  ])
})

test('addresses are sent one per line with the mode, and none are sent as no mode', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByRole('combobox', { name: 'What the list does' }).click()
  await page.getByRole('option', { name: 'Block these addresses' }).click()
  await page.getByLabel('Addresses').fill('198.51.100.7\n\n 203.0.113.0/24 ')
  await page.getByRole('button', { name: 'Save options' }).click()
  await expect(page.getByText('The options were saved.')).toBeVisible()

  expect(stub.saved[0].ipAccessMode).toBe('denyList')
  expect(stub.saved[0].ipRules).toEqual(['198.51.100.7', '203.0.113.0/24'])

  await page.getByLabel('Addresses').fill('')
  await page.getByRole('button', { name: 'Save options' }).click()
  await expect.poll(() => {
    return stub.saved.length
  }).toBe(2)
  expect(stub.saved[1].ipAccessMode).toBeNull()
  expect(stub.saved[1].ipRules).toEqual([])
})

test('a value that is not a whole number is not sent and is explained', async ({ page }) => {
  const stub = await stubOptionsTab(page)
  await openOptions(page)

  await page.getByLabel('Largest upload (MiB)').fill('lots')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByText('Enter a whole number, or leave the field empty.')).toBeVisible()
  expect(stub.saved).toHaveLength(0)
})

test('the panel refusal of a redirect is shown under the redirects with its own words', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/sites\/[^/]+\/options$/)
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubSiteOptionsRefusal(page, 'SiteRedirectPathInvalid', 'One of the redirect paths is not valid.')
  await openOptions(page)

  await page.getByRole('button', { name: 'Add a redirect' }).click()
  await page.getByLabel('Path of rule 1').fill('bad')
  await page.getByLabel('Send to, rule 1').fill('/x')
  await page.getByRole('button', { name: 'Save options' }).click()

  await expect(page.getByRole('alert').filter({ hasText: 'One of the redirect paths is not valid.' })).toBeVisible()
  // The refusal keeps what was typed, so the operator fixes one field rather than retyping the form.
  await expect(page.getByLabel('Path of rule 1')).toHaveValue('bad')
  await expect(page.getByText('The options were saved.')).toHaveCount(0)
})
