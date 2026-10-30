import { expect, test, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const SITE: SiteDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: ['www.alpha.example.com'],
  backendType: 'php',
  phpVersion: '8.3',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Installs the routes the detail page reads, narrowest last so it wins Playwright's ordering.
 * @param page The Playwright page whose network the routes are installed on.
 * @returns Resolves once every route is installed.
 */
const stubDetailDependencies = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [
    { version: '8.2', isDefault: false },
    { version: '8.3', isDefault: true },
  ])
}

test('the site overview shows what the panel reported, including its document root', async ({ page }) => {
  await stubDetailDependencies(page)

  await page.goto(`/sites/${SITE.id}`)

  await expect(page.getByRole('heading', { name: SITE.domain })).toBeVisible()
  await expect(page.getByText(SITE.documentRoot)).toBeVisible()
  await expect(page.getByText('www.alpha.example.com')).toBeVisible()
  await expect(page.getByText('Not installed')).toBeVisible()
})

// The consequence, not "are you sure": an operator who believes deleting a site wipes the
// customer's files will hesitate over a safe action, and one who believes the opposite will not
// hesitate over a destructive one. The contract removes the vhost and leaves the files.
test('deleting a site asks in a dialog that names it and says the files are left on disk', async ({
  page,
}) => {
  await stubDetailDependencies(page)

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()

  // The accessible name says which site, not "Confirm".
  const dialog = page.getByRole('dialog', { name: `Delete site ${SITE.domain}` })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText('The files in the document root are left on disk.')
  // Nothing has been sent yet: the confirmation is a question, not a progress indicator.
  await expect(page).toHaveURL(new RegExp(`/sites/${SITE.id}$`))

  await dialog.getByRole('button', { name: 'Delete site' }).click()

  await expect(page).toHaveURL(/\/sites$/)
})

// Disabling and deleting are different questions, and the dialog must ask the one the operator
// started — a shared component that showed the last question asked would be worse than five
// hand-written ones.
test('disabling asks its own question, not the deletion one', async ({ page }) => {
  await stubDetailDependencies(page)

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Disable' }).click()

  const dialog = page.getByRole('dialog', { name: `Disable site ${SITE.domain}` })
  await expect(dialog).toContainText('It keeps its vhost and answers with a suspension response.')
  await expect(dialog).not.toContainText('left on disk')
})

test('abandoning a delete sends nothing and leaves the site alone', async ({ page }) => {
  await stubDetailDependencies(page)
  let deleteRequests = 0
  await page.route(`**/api/v1/sites/${SITE.id}`, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }
    deleteRequests += 1
    await route.fulfill({ status: 204, body: '' })
  })

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deleteRequests).toBe(0)
  await expect(page.getByRole('button', { name: 'Delete' })).toBeVisible()
  await expect(page).toHaveURL(new RegExp(`/sites/${SITE.id}$`))
})

test('a PHP site offers to rebind only to a version this host has installed', async ({ page }) => {
  await stubDetailDependencies(page)

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('combobox', { name: 'PHP version' }).click()

  await expect(page.getByRole('option', { name: 'PHP 8.2' })).toBeVisible()
  await expect(page.getByRole('option', { name: 'PHP 8.3 (server default)' })).toBeVisible()
  await expect(page.getByRole('option')).toHaveCount(2)
})

// Issue #204, item 6: a site id is a UUID, so `/sites/php` cannot name a site. The router refuses
// the shape itself and shows the not-found screen, without asking the panel for a site called "php".
test('a site address that is not a UUID shows the not-found screen without asking the panel', async ({ page }) => {
  await stubDetailDependencies(page)
  const siteReads: string[] = []
  page.on('request', (request) => {
    if (/\/api\/v1\/sites\/[^/?]+$/.test(request.url())) {
      siteReads.push(request.url())
    }
  })

  await page.goto('/sites/php')

  await expect(page.getByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible()
  await expect(page.getByText('Not Found', { exact: true })).toHaveCount(0)
  expect(siteReads).toEqual([])
})

// A well-formed id the panel does not know answers 404, and that is the site's not-found state —
// never an error alert repeating the transport's "Not Found" (issue #204, item 6).
test('an unknown site id shows the site not-found state rather than an error alert', async ({ page }) => {
  const unknownId = '44444444-4444-4444-4444-444444444444'
  await stubDetailDependencies(page)
  await page.route(`**/api/v1/sites/${unknownId}`, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'SiteNotFound', title: 'Not Found', status: 404 }),
    })
  })

  await page.goto(`/sites/${unknownId}`)

  await expect(page.getByText('Site not found')).toBeVisible()
  // The problem's own title is what the error alert used to print; the not-found state never does.
  await expect(page.getByText('Not Found', { exact: true })).toHaveCount(0)
})

// The inverse control for the test above: a refusal that is NOT a 404 is still an error, shown
// with the panel's own text, so the not-found state is not simply what every failure renders as.
test('a site read the panel fails for another reason shows the panel message as an error', async ({ page }) => {
  const failingId = '77777777-7777-7777-7777-777777777777'
  await stubDetailDependencies(page)
  await page.route(`**/api/v1/sites/${failingId}`, async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AgentUnavailable', detail: 'The server agent is not answering.', status: 503 }),
    })
  })

  await page.goto(`/sites/${failingId}`)

  await expect(page.getByText('The server agent is not answering.')).toBeVisible()
  await expect(page.getByText('Site not found')).toHaveCount(0)
})
