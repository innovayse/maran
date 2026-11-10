import { expect, test } from '../fixtures/guarded-test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import {
  CERTIFICATE_SITE_ID,
  certificateFor,
  stubAcmeDirectory,
  stubCertificates,
  stubCertificatesProblem,
  stubCustomCertificateRefusal,
  stubIssuanceOptions,
} from '../fixtures/stub-certificates-routes'
import type { AcmeDirectory, Certificate } from '../../src/types/certificate'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'
import type { Page } from '@playwright/test'

// These four tests used to assert "Certificates are not available on this server yet." — the
// message the SPA's certificates composable produced because it was still a Task-14 seam that
// rejected every call. The Ssl module, its controller and its four endpoints existed by then, so
// the suite was green BECAUSE the feature was unreachable: an operator whose site was being
// served over TLS opened this tab and was told the panel had no certificates. The tests are
// rewritten here to assert what the tab does now, not deleted, because each one still names a
// real behaviour the screen has to have.

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
]

const SITE: SiteDetail = {
  id: CERTIFICATE_SITE_ID,
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
  logRetentionDays: 14,
}

/** The ACME directory an administrator's tab reads beside the certificates. */
const DIRECTORY: AcmeDirectory = {
  url: 'https://acme-staging-v02.api.letsencrypt.org/directory',
  host: 'acme-staging-v02.api.letsencrypt.org',
  kind: 'staging',
  kindName: 'Staging (test certificates)',
}

/**
 * Installs the shell, the site and the certificate endpoints.
 * @param page The Playwright page whose network the routes are installed on.
 * @param installed The certificates the panel reports for the site; mutated by the calls.
 * @returns Resolves once every route is installed.
 */
const stubSslTab = async (page: Page, installed: Certificate[] = []): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubCertificates(page, installed)
  await stubAcmeDirectory(page, DIRECTORY)
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
  guard,
}) => {
  guard.allow(500, /\/api\/v1\/certificates(\?.*)?$/)
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

  expect((await issued).postDataJSON()).toEqual({ domain: SITE.domain, wildcard: false })
})

test('removing a certificate asks which one, then takes it off the screen', async ({ page }) => {
  await stubSslTab(page, [certificateFor(SITE.domain, 'custom')])

  await openSsl(page)
  await page.getByRole('button', { name: 'Remove certificate' }).click()

  const dialog = page.getByRole('dialog', { name: `Remove the certificate for ${SITE.domain}` })
  await expect(dialog).toContainText('The site goes back to serving plain HTTP.')
  await dialog.getByRole('button', { name: 'Remove certificate' }).click()

  await expect(page.getByText('No certificate installed')).toBeVisible()
})

// A certificate is what makes the site reachable over HTTPS, so a dismissed question must leave
// it installed and must not have sent the removal.
test('dismissing the removal keeps the certificate and sends nothing', async ({ page }) => {
  const certificate = certificateFor(SITE.domain, 'custom')
  await stubSslTab(page, [certificate])
  let deleteRequests = 0
  await page.route(`**/api/v1/certificates/${certificate.id}`, async (route) => {
    deleteRequests += 1
    await route.fulfill({ status: 204, body: '' })
  })

  await openSsl(page)
  await page.getByRole('button', { name: 'Remove certificate' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deleteRequests).toBe(0)
  await expect(page.getByText('No certificate installed')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Remove certificate' })).toBeVisible()
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

// Issue #214: a refusal used to empty both text areas, after which the form's own "required"
// check reported the text the operator had just sent as missing.
test('a pem the panel refuses is shown under its own field and the pasted text is kept', async ({
  page,
  guard,
}) => {
  guard.allow(400, /\/api\/v1\/certificates\/custom$/)
  const detail =
    'The certificate chain is not valid PEM: it must contain a block that begins with -----BEGIN CERTIFICATE-----.'
  await stubSslTab(page, [])
  await stubCustomCertificateRefusal(page, 'CertificatePemInvalid', detail)

  await openSsl(page)
  await page.getByRole('button', { name: 'Install my own certificate' }).click()
  await page.getByLabel('Certificate chain (PEM)').fill('not a certificate')
  await page.getByLabel('Private key (PEM)').fill('-----BEGIN PRIVATE KEY-----')
  await page.getByRole('button', { name: 'Install certificate' }).click()

  await expect(page.getByText(detail)).toHaveCount(1)
  await expect(page.getByLabel('Certificate chain (PEM)')).toHaveAccessibleDescription(detail)
  await expect(page.getByLabel('Certificate chain (PEM)')).toHaveValue('not a certificate')
  await expect(page.getByLabel('Private key (PEM)')).toHaveValue('-----BEGIN PRIVATE KEY-----')
  await expect(page.getByText('The certificate chain is required.')).toHaveCount(0)
  await expect(page.getByText('The private key is required.')).toHaveCount(0)
})

test('a key that does not match the certificate is shown under the key field', async ({ page, guard }) => {
  guard.allow(400, /\/api\/v1\/certificates\/custom$/)
  const detail =
    'The private key does not belong to this certificate. Paste the key that was created together with the certificate request.'
  await stubSslTab(page, [])
  await stubCustomCertificateRefusal(page, 'AgentPrivateKeyMismatch', detail)

  await openSsl(page)
  await page.getByRole('button', { name: 'Install my own certificate' }).click()
  await page.getByLabel('Certificate chain (PEM)').fill('-----BEGIN CERTIFICATE-----')
  await page.getByLabel('Private key (PEM)').fill('-----BEGIN PRIVATE KEY-----')
  await page.getByRole('button', { name: 'Install certificate' }).click()

  await expect(page.getByLabel('Private key (PEM)')).toHaveAccessibleDescription(detail)
  await expect(page.getByLabel('Certificate chain (PEM)')).not.toHaveAccessibleDescription(detail)
  await expect(page.getByLabel('Private key (PEM)')).toHaveValue('-----BEGIN PRIVATE KEY-----')
})

// Issue #161: a wildcard certificate is validated through the DNS zone the panel serves, so only the
// panel knows whether the choice can work. The tab offers it only when the panel says so, and says
// the panel's own reason when it does not.
test('the wildcard choice is offered only when the panel says it can work, and then it is sent', async ({
  page,
}) => {
  await stubSslTab(page, [])
  await stubIssuanceOptions(page, {
    domain: SITE.domain,
    wildcardAvailable: true,
    wildcardUnavailableCode: null,
    wildcardUnavailableMessage: null,
  })
  const issued = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().endsWith('/api/v1/certificates')
  })

  await openSsl(page)
  await page.getByRole('checkbox', { name: 'Also cover every name under this domain (wildcard)' }).check({ force: true })
  await page.getByRole('button', { name: 'Request a certificate' }).click()

  expect((await issued).postDataJSON()).toEqual({ domain: SITE.domain, wildcard: true })
  await expect(page.getByText(`Wildcard: *.${SITE.domain}`)).toBeVisible()
})

test('the wildcard choice is absent and the panels reason is shown when the panel cannot do it', async ({
  page,
}) => {
  const reason = 'This server does not serve the DNS zone of this domain.'
  await stubSslTab(page, [])
  await stubIssuanceOptions(page, {
    domain: SITE.domain,
    wildcardAvailable: false,
    wildcardUnavailableCode: 'WildcardZoneNotServed',
    wildcardUnavailableMessage: reason,
  })

  await openSsl(page)

  await expect(page.getByText(reason)).toBeVisible()
  await expect(page.getByRole('checkbox', { name: 'Also cover every name under this domain (wildcard)' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Request a certificate' })).toBeVisible()
})

test('a plain request stays plain when the wildcard choice was never ticked', async ({ page }) => {
  await stubSslTab(page, [])
  await stubIssuanceOptions(page, {
    domain: SITE.domain,
    wildcardAvailable: true,
    wildcardUnavailableCode: null,
    wildcardUnavailableMessage: null,
  })
  const issued = page.waitForRequest((request) => {
    return request.method() === 'POST' && request.url().endsWith('/api/v1/certificates')
  })

  await openSsl(page)
  await expect(page.getByRole('checkbox', { name: 'Also cover every name under this domain (wildcard)' })).toBeVisible()
  await page.getByRole('button', { name: 'Request a certificate' }).click()

  expect((await issued).postDataJSON()).toEqual({ domain: SITE.domain, wildcard: false })
  await expect(page.getByText(`Wildcard: *.${SITE.domain}`)).toHaveCount(0)
})
