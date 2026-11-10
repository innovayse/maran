import { expect, test } from '../fixtures/guarded-test'
import { openSection, mainNavigation } from '../fixtures/open-full-shell'
import { openApps } from '../fixtures/open-apps'
import { stubAccounts } from '../fixtures/stub-accounts-route'
import { appsWorld } from '../fixtures/stub-apps-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { READY_STACK } from '../fixtures/stub-stack-routes'
import {
  DEFAULT_SITE_CHOICES,
  stubPhpVersions,
  stubSiteChoices,
  stubSiteLimits,
  stubSites,
} from '../fixtures/stub-sites-routes'
import type { Account } from '../../src/types/account'
import type { SiteChoices } from '../../src/types/site'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// Issue #517: runtimes are apps, so the site form and the menu offer a runtime only while it is
// installed, and the form says which web server the host runs. Each hidden option is paired with the
// same screen on a host that has the runtime, so a form that hides everything cannot pass.

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const OWNER: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alpha',
  primaryDomain: 'alpha.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** Every backend, and every runtime installed, as the panel's choices state them for a full host. */
const FULL_CHOICES: SiteChoices = {
  ...DEFAULT_SITE_CHOICES,
  backendTypes: [
    { value: 'static', label: 'Static' },
    { value: 'php', label: 'PHP' },
    { value: 'node', label: 'Node.js' },
    { value: 'reverseProxy', label: 'Reverse proxy' },
  ],
  phpVersions: [{ value: '8.3', label: 'PHP 8.3' }],
  nodeVersions: [{ value: '22', label: 'Node.js 22' }],
  webServer: { value: 'nginx', label: 'nginx' },
}

/**
 * Installs everything the create-site form reads, with the choices the panel states for the host.
 * @param page The Playwright page whose network the routes are installed on.
 * @param choices The closed sets the panel offers; it leaves out a backend whose runtime is not installed.
 * @returns Resolves once the form is drawn.
 */
const openSiteForm = async (page: Page, choices: SiteChoices): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubAccounts(page, [OWNER])
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubSiteLimits(page, { maximumAliases: 10 })
  // The sites stub installs default choices of its own, so the choices under test are installed after it.
  await stubSites(page, [])
  await stubSiteChoices(page, choices)
  await page.goto('/sites/new')
  await expect(page.getByRole('combobox', { name: 'Backend' })).toBeVisible()
}

test('the site form offers only the backends the panel says the host can serve', async ({ page }) => {
  await openSiteForm(page, {
    ...FULL_CHOICES,
    backendTypes: FULL_CHOICES.backendTypes.filter((choice) => {
      return choice.value !== 'php'
    }),
    phpVersions: [],
  })

  await page.getByRole('combobox', { name: 'Backend' }).click()
  await expect(page.getByRole('option', { name: 'Node.js' })).toBeVisible()
  await expect(page.getByRole('option', { name: 'PHP', exact: true })).toHaveCount(0)
})

test('the site form offers both runtimes when both are installed', async ({ page }) => {
  await openSiteForm(page, FULL_CHOICES)

  await page.getByRole('combobox', { name: 'Backend' }).click()
  await expect(page.getByRole('option', { name: 'PHP', exact: true })).toBeVisible()
  await expect(page.getByRole('option', { name: 'Node.js' })).toBeVisible()
})

test('the site form names the web server the host runs', async ({ page }) => {
  await openSiteForm(page, { ...FULL_CHOICES, webServer: { value: 'apache', label: 'Apache' } })

  await expect(page.getByText('Web server', { exact: true })).toBeVisible()
  await expect(page.getByText('Apache', { exact: true })).toBeVisible()
})

test('a Node.js site asks for a version, a port and a start command, and sends them', async ({ page }) => {
  await openSiteForm(page, FULL_CHOICES)
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alpha/ }).click()
  await page.getByLabel('Domain', { exact: true }).fill('app.example.com')
  await page.getByRole('combobox', { name: 'Backend' }).click()
  await page.getByRole('option', { name: 'Node.js' }).click()

  // Refused first: the three Node.js fields are required, and a privileged port is not a port the app can bind.
  await page.getByLabel('App port').fill('80')
  await page.getByRole('button', { name: 'Create site' }).click()
  await expect(page.getByText('Choose an installed Node.js version.')).toBeVisible()
  await expect(page.getByText('The app port must be a whole number from 1024 to 65535.')).toBeVisible()
  await expect(page.getByText('The start command is required.')).toBeVisible()

  await page.getByRole('combobox', { name: 'Node.js version' }).click()
  await page.getByRole('option', { name: 'Node.js 22' }).click()
  await page.getByLabel('App port').fill('3000')
  await page.getByLabel('Start command').fill('npm start')
  const created = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().endsWith('/api/v1/sites')
  })
  await page.getByRole('button', { name: 'Create site' }).click()

  const body = (await created).postDataJSON() as Record<string, unknown>
  expect(body).toMatchObject({
    backendType: 'node',
    nodeVersion: '22',
    appPort: 3000,
    startCommand: 'npm start',
    phpVersion: '',
  })
})

test('a site that is not a Node.js site sends no Node.js fields', async ({ page }) => {
  await openSiteForm(page, FULL_CHOICES)
  await page.getByRole('combobox', { name: 'Account' }).click()
  await page.getByRole('option', { name: /alpha/ }).click()
  await page.getByLabel('Domain', { exact: true }).fill('static.example.com')
  const created = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().endsWith('/api/v1/sites')
  })
  await page.getByRole('button', { name: 'Create site' }).click()

  const body = (await created).postDataJSON() as Record<string, unknown>
  expect(body).toMatchObject({ backendType: 'static', nodeVersion: '', appPort: 0, startCommand: '' })
})

test('the menu offers the PHP pages only while a PHP version is installed', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/', stack: { ...READY_STACK, phpVersions: [] } })
  await openSection(page, 'Sites and domains')
  await expect(mainNavigation(page).getByRole('link', { name: 'PHP versions' })).toHaveCount(0)
})

test('the menu offers the PHP pages on a host that has PHP', async ({ page }) => {
  await openApps(page, appsWorld(), { path: '/', stack: READY_STACK })
  await openSection(page, 'Sites and domains')
  await expect(mainNavigation(page).getByRole('link', { name: 'PHP versions' })).toBeVisible()
})
