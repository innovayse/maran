import { expect, test } from '../fixtures/guarded-test'
import { ONE_SHOT, stubDashboard } from '../fixtures/stub-dashboard-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubIdleLiveDashboard } from '../fixtures/stub-live-dashboard-stream'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import {
  completePhpVersion,
  stubInstallablePhpVersions,
  stubPhpVersions,
  type PhpVersionStub,
} from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { InstallablePhpVersion } from '../../src/types/phpVersion'
import type { Page } from '@playwright/test'

// Installing a PHP version is a choice from the versions the agent reports as installable (#330):
// the operator never types one. The spec reaches the screen by clicking through the sidebar, as an
// operator does, and chooses the version by clicking the option the backend offered.

const MODULES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

/** What the stubbed server has installed to begin with. */
const INSTALLED: PhpVersionStub[] = [{ version: '8.3', defaultStatus: 'unknown' }]

/** The versions the stubbed agent reports as installable, with 8.3 already installed. */
const INSTALLABLE: InstallablePhpVersion[] = [
  { version: '8.4', installed: false },
  { version: '8.3', installed: true },
  { version: '8.2', installed: false },
]

/**
 * Signs in as an administrator, stubs the PHP endpoints with a server that installs what it is told
 * to, and opens the PHP versions screen through the sidebar.
 * @param page The Playwright page under test.
 * @returns The bodies the install endpoint received, appended to as the operator installs.
 */
const openPhpVersions = async (page: Page): Promise<unknown[]> => {
  const installed: PhpVersionStub[] = [...INSTALLED]
  const installable: InstallablePhpVersion[] = INSTALLABLE.map((entry) => {
    return { ...entry }
  })
  const installBodies: unknown[] = []

  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, MODULES)
  // The walk starts on the dashboard, so the administrator's dashboard reads are answered too.
  await stubDashboard(page, ONE_SHOT)
  await stubIdleLiveDashboard(page)
  await stubPhpVersions(page, installed)
  await stubInstallablePhpVersions(page, () => {return installable})
  await page.route('**/api/v1/sites/php-versions/install', async (route) => {
    const body = route.request().postDataJSON() as { version: string }
    installBodies.push(body)
    installed.push({ version: body.version, defaultStatus: 'unknown' })
    const entry = installable.find((candidate) => {
      return candidate.version === body.version
    })
    if (entry) {
      entry.installed = true
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(installed.map(completePhpVersion)) })
  })

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Sites and domains pages' }).click()
  await nav.getByRole('link', { name: 'PHP versions' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'PHP versions' })).toBeVisible()

  return installBodies
}

test('the install control is a select, not a text field', async ({ page }) => {
  await openPhpVersions(page)

  await expect(page.getByRole('combobox', { name: 'Version to install' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Version to install' })).toHaveCount(0)
})

test('the select offers only the installable versions that are not installed yet', async ({ page }) => {
  await openPhpVersions(page)

  await page.getByRole('combobox', { name: 'Version to install' }).click()

  await expect(page.getByRole('option')).toHaveText(['PHP 8.4', 'PHP 8.2'])
})

test('choosing an option and pressing install sends exactly that version and drops it from the list', async ({
  page,
}) => {
  const installBodies = await openPhpVersions(page)
  const install = page.getByRole('button', { name: 'Install this version' })
  await expect(install).toBeDisabled()

  await page.getByRole('combobox', { name: 'Version to install' }).click()
  await page.getByRole('option', { name: 'PHP 8.4' }).click()
  await expect(install).toBeEnabled()
  await install.click()

  await expect.poll(() => {return installBodies}).toEqual([{ version: '8.4' }])
  await page.getByRole('combobox', { name: 'Version to install' }).click()
  await expect(page.getByRole('option')).toHaveText(['PHP 8.2'])
})

test('an install the host cannot carry out shows the server\'s own sentence and leaves the list as it was', async ({
  page,
  guard,
}) => {
  guard.allow(409, /\/api\/v1\/sites\/php-versions\/install$/)
  // Issue #466: a package no source of the host carries is a 409 with a typed code, and the screen
  // renders the sentence the backend localised, not a generic "contact support" alert.
  const sentence = 'None of the package repositories configured on your server carries the software you asked to install, so nothing was installed.'
  await openPhpVersions(page)
  await page.route('**/api/v1/sites/php-versions/install', async (route) => {
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 409, code: 'AgentPackageNotAvailable', title: sentence }),
    })
  })

  await page.getByRole('combobox', { name: 'Version to install' }).click()
  await page.getByRole('option', { name: 'PHP 8.2' }).click()
  await page.getByRole('button', { name: 'Install this version' }).click()

  await expect(page.getByText(sentence)).toBeVisible()
  await expect(page.getByText(/contact support/i)).toHaveCount(0)
  await page.getByRole('combobox', { name: 'Version to install' }).click()
  await expect(page.getByRole('option')).toHaveText(['PHP 8.4', 'PHP 8.2'])
})
