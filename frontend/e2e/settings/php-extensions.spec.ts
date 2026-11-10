import { expect, test } from '../fixtures/guarded-test'
import { holdRoute } from '../fixtures/hold-route'
import { stubPhpExtensions, type PhpExtensionsStub } from '../fixtures/stub-php-extension-routes'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubLandingReads } from '../fixtures/stub-landing-reads'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubInstallablePhpVersions, stubPhpVersions } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { PhpExtension } from '../../src/types/phpVersion'
import type { Page } from '@playwright/test'

// Issue #157: an administrator switches the extensions of an installed PHP version from the PHP
// versions screen. The labels and what can be done to each row are the panel's; the SPA draws a
// switch where the row is changeable, a plain sentence where it is not, and the state the server
// reports after the change. The spec reaches the screen by clicking through the sidebar.

const MODULES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

/** The extensions the stubbed server starts with: one of each thing a row can be. */
const startingExtensions = (): PhpExtension[] => {
  return [
    { name: 'gd', label: 'Images (GD)', enabled: false, availability: 'available' },
    { name: 'intl', label: 'Internationalisation (intl)', enabled: true, availability: 'available' },
    { name: 'imagick', label: 'ImageMagick', enabled: false, availability: 'notPackaged' },
    { name: 'opcache', label: 'OPcache', enabled: true, availability: 'disableNotSupported' },
    { name: 'redis', label: 'Redis', enabled: false, availability: 'disableNotSupported' },
  ]
}

/**
 * Signs in as an administrator, stubs the PHP endpoints and opens the extensions of PHP 8.3
 * through the sidebar and the version's own row.
 * @param page The Playwright page under test.
 * @param extensions The extensions the stubbed server starts with.
 * @param refusal The sentence the stubbed server refuses every switch with, or `null` to accept.
 * @returns The stub, for the spec to read the requests from.
 */
const openExtensions = async (
  page: Page,
  extensions: PhpExtension[] = startingExtensions(),
  refusal: string | null = null,
): Promise<PhpExtensionsStub> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubModules(page, MODULES)
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubInstallablePhpVersions(page, () => {
    return []
  })
  const stub = await stubPhpExtensions(page, extensions, refusal)

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Sites and domains pages' }).click()
  await nav.getByRole('link', { name: 'PHP versions' }).click()
  await page.getByRole('button', { name: 'Extensions of 8.3' }).click()
  await expect(page.getByRole('heading', { name: 'Extensions of PHP 8.3' })).toBeVisible()

  return stub
}

test('every extension is a switch named by the label the panel sent, in the state it reports', async ({ page }) => {
  await openExtensions(page)

  await expect(page.getByRole('switch', { name: 'Images (GD)' })).toHaveAttribute('aria-checked', 'false')
  await expect(page.getByRole('switch', { name: 'Internationalisation (intl)' })).toHaveAttribute(
    'aria-checked',
    'true',
  )
  await expect(page.getByRole('switch')).toHaveCount(5)
})

test('switching an extension on sends that name and state, and the switch shows the state read back', async ({
  page,
}) => {
  const stub = await openExtensions(page)

  await page.getByRole('switch', { name: 'Images (GD)' }).click()

  await expect.poll(() => {
    return stub.requests
  }).toEqual([{ name: 'gd', enabled: true }])
  await expect(page.getByRole('switch', { name: 'Images (GD)' })).toHaveAttribute('aria-checked', 'true')
})

test('an extension no repository offers has a held switch and says why in text', async ({ page }) => {
  await openExtensions(page)

  await expect(page.getByRole('switch', { name: 'ImageMagick' })).toBeDisabled()
  await expect(page.getByText("This server's package repository does not offer this extension.")).toBeVisible()
})

test('an extension that is on but cannot be switched off is held with its reason, and one that is off can be switched on', async ({
  page,
}) => {
  await openExtensions(page)

  await expect(page.getByRole('switch', { name: 'OPcache' })).toBeDisabled()
  await expect(page.getByText("This system's PHP packages cannot switch this extension off.")).toBeVisible()
  await expect(page.getByRole('switch', { name: 'Redis' })).toBeEnabled()
})

test('a refused switch prints the panel sentence verbatim and leaves the switch as it was', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/sites\/php-versions\/[^/]+\/extensions\/[^/]+$/)
  const refusal = 'PHP refused to reload with this extension: the configuration test failed.'
  const stub = await openExtensions(page, startingExtensions(), refusal)

  await page.getByRole('switch', { name: 'Images (GD)' }).click()

  await expect(page.getByText(refusal)).toBeVisible()
  expect(stub.requests).toEqual([{ name: 'gd', enabled: true }])
  await expect(page.getByRole('switch', { name: 'Images (GD)' })).toHaveAttribute('aria-checked', 'false')
})

test('while a change applies every switch is held and a notice points to the tasks screen', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubModules(page, MODULES)
  await stubPhpVersions(page, [{ version: '8.3', defaultStatus: 'default' }])
  await stubInstallablePhpVersions(page, () => {
    return []
  })
  await stubPhpExtensions(page, startingExtensions())
  // Installed AFTER the stub so that it holds the request and then hands it on to the stub.
  const held = await holdRoute(page, '**/api/v1/sites/php-versions/*/extensions/*')

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await nav.getByRole('button', { name: 'Show the Sites and domains pages' }).click()
  await nav.getByRole('link', { name: 'PHP versions' }).click()
  await page.getByRole('button', { name: 'Extensions of 8.3' }).click()

  await page.getByRole('switch', { name: 'Images (GD)' }).click()
  await held.requested

  await expect(page.getByText('Applying Images (GD) for PHP 8.3.', { exact: false })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open the tasks screen' })).toBeVisible()
  await expect(page.getByRole('switch', { name: 'Internationalisation (intl)' })).toBeDisabled()

  held.release()
  await expect(page.getByText('Applying Images (GD) for PHP 8.3.', { exact: false })).toHaveCount(0)
  await expect(page.getByRole('switch', { name: 'Internationalisation (intl)' })).toBeEnabled()
})

test('a list without availability is read-only: the state is a word and there are no switches', async ({ page }) => {
  await openExtensions(page, [
    { name: 'gd', label: 'Images (GD)', enabled: true, availability: null },
    { name: 'redis', label: 'Redis', enabled: false, availability: null },
  ])

  await expect(page.getByRole('switch')).toHaveCount(0)
  await expect(page.getByText('Images (GD)', { exact: true })).toBeVisible()
  await expect(page.getByText('On', { exact: true })).toBeVisible()
  await expect(page.getByText('Off', { exact: true })).toBeVisible()
})
