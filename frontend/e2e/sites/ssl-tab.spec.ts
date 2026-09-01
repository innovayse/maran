import { expect, test, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import {
  CERTIFICATE_SITE_ID,
  certificateFor,
  stubCertificates,
  stubCertificatesProblem,
} from '../fixtures/stub-certificates-routes'
import type { Certificate } from '../../src/types/certificate'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

// These four tests used to assert "Certificates are not available on this server yet." — the
// message the SPA's certificates composable produced because it was still a Task-14 seam that
// rejected every call. The Ssl module, its controller and its four endpoints existed by then, so
// the suite was green BECAUSE the feature was unreachable: an operator whose site was being
// served over TLS opened this tab and was told the panel had no certificates. The tests are
// rewritten here to assert what the tab does now, not deleted, because each one still names a
// real behaviour the screen has to have.

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const SITE: SiteDetail = {
  id: CERTIFICATE_SITE_ID,
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: [],
  backendType: 'static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Installs the shell, the site and the certificate endpoints.
 * @param page The Playwright page whose network the routes are installed on.
 * @param installed The certificates the panel reports for the site; mutated by the calls.
 * @returns Resolves once every route is installed.
 */
const stubSslTab = async (page: Page, installed: Certificate[] = []): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubCertificates(page, installed)
}

/**
 * Opens the site's SSL tab.
 * @param page The Playwright page to drive.
 * @returns Resolves once the tab is on screen.
 */
const openSsl = async (page: Page): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'SSL' }).click()
}

test('the SSL tab shows the certificate the panel reports for the site', async ({ page }) => {
  await stubSslTab(page, [certificateFor(SITE.domain, 'acme')])

  await openSsl(page)

  // The domain appears in the page heading too, so this asks for the certificate card's own
  // definition list rather than for the text anywhere on the screen.
  await expect(page.getByRole('definition').filter({ hasText: SITE.domain })).toBeVisible()
  await expect(page.getByText('Issued by the panel')).toBeVisible()
  await expect(page.getByText('No certificate installed')).toHaveCount(0)
})

test('the SSL tab says no certificate is installed only when the panel answered with none', async ({
  page,
}) => {
  await stubSslTab(page, [])

  await openSsl(page)

  await expect(page.getByText('No certificate installed')).toBeVisible()
})

test('the SSL tab renders the panels own message when the certificates call fails', async ({
  page,
}) => {
  const consoleErrors: string[] = []
  page.on('pageerror', (error) => {
    consoleErrors.push(error.message)
  })
  await stubSslTab(page, [])
  // Installed last, so it wins over the working stub above.
  await stubCertificatesProblem(page, 'The certificate store is unreachable.')

  await openSsl(page)

  // Verbatim, and no invented copy: the backend owns every word a user reads.
  await expect(page.getByText('The certificate store is unreachable.')).toBeVisible()
  await expect(page.getByText('No certificate installed')).toHaveCount(0)
  // The tab is still interactive: a failed read is a state, not a broken screen.
  await expect(page.getByRole('button', { name: 'Request a certificate' })).toBeVisible()
  expect(consoleErrors).toEqual([])
})

test('requesting a certificate installs it and the tab shows it without a reload', async ({
  page,
}) => {
  await stubSslTab(page, [])

  await openSsl(page)
  await expect(page.getByText('No certificate installed')).toBeVisible()
  await page.getByRole('button', { name: 'Request a certificate' }).click()

  await expect(page.getByText('Issued by the panel')).toBeVisible()
  await expect(page.getByText('No certificate installed')).toHaveCount(0)
})

test('the certificate request names the site domain the panel addresses certificates by', async ({
  page,
}) => {
  // The endpoint takes a DOMAIN, not a site id, and the tab is given the site's domain as a prop
  // for exactly that reason. A request that sent an id would be accepted by a stub that ignores
  // its body and refused by the real panel with "site not found".
  await stubSslTab(page, [])
  const issued = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().endsWith('/api/v1/certificates')
  })

  await openSsl(page)
  await page.getByRole('button', { name: 'Request a certificate' }).click()

  expect((await issued).postDataJSON()).toEqual({ domain: SITE.domain })
})

test('removing a certificate takes it off the screen once the panel confirms', async ({ page }) => {
  await stubSslTab(page, [certificateFor(SITE.domain, 'custom')])

  await openSsl(page)
  await page.getByRole('button', { name: 'Remove certificate' }).click()
  await page.getByRole('button', { name: 'Yes, do it' }).click()

  await expect(page.getByText('No certificate installed')).toBeVisible()
})

// The private key is typed, sent and forgotten. Nothing in the panel displays one, and a key held
// after the request settles outlives the form that sent it.
test('the upload form clears the private key once the attempt has settled', async ({ page }) => {
  await stubSslTab(page, [])

  await openSsl(page)
  await page.getByRole('button', { name: 'Install my own certificate' }).click()
  await page.getByLabel('Certificate chain (PEM)').fill('-----BEGIN CERTIFICATE-----')
  await page.getByLabel('Private key (PEM)').fill('-----BEGIN PRIVATE KEY-----')
  await page.getByRole('button', { name: 'Install certificate' }).click()

  await expect(page.getByText('Uploaded')).toBeVisible()
  await expect(page.getByLabel('Private key (PEM)')).toHaveCount(0)
})

test('the upload form validates its own empty fields rather than the browser doing it', async ({
  page,
}) => {
  await stubSslTab(page, [])

  await openSsl(page)
  await page.getByRole('button', { name: 'Install my own certificate' }).click()
  await page.getByRole('button', { name: 'Install certificate' }).click()

  await expect(page.getByText('The certificate chain is required.')).toBeVisible()
  await expect(page.getByText('The private key is required.')).toBeVisible()
})
