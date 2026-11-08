import { expect, test, type Page } from '@playwright/test'
import { expectNoSeriousViolations } from '../fixtures/expect-no-serious-violations'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  CERTIFICATE_SITE_ID,
  certificateFor,
  stubAcmeDirectory,
  stubAcmeDirectoryFailure,
  stubCertificates,
} from '../fixtures/stub-certificates-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { AcmeDirectory, Certificate } from '../../src/types/certificate'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

// Issue #161: the certificate card reports the last renewal attempt, how many attempts failed in a
// row and the panel's own sentence for the failure, and an administrator sees which ACME directory
// the panel orders from. Every absence below is asserted next to a positive control in the same
// test, so a screen that stopped drawing the card cannot pass for "nothing shown".

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
  hasCertificate: true,
  status: 'enabled',
  statusName: 'Enabled',
  createdAt: '2026-08-01T10:00:00Z',
  logRetentionDays: 14,
}

const CUSTOMER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000099',
  username: 'olive',
  email: 'olive@example.com',
  role: 'customer',
  accountId: SITE.accountId,
}

const DIRECTORY: AcmeDirectory = {
  url: 'https://acme-staging-v02.api.letsencrypt.org/directory',
  host: 'acme-staging-v02.api.letsencrypt.org',
  kind: 'staging',
  kindName: 'Staging (test certificates)',
}

const FAILURE_SENTENCE = 'The certificate authority could not reach the site to confirm the domain.'

/** A panel-issued certificate far from its end, so only the renewal failure can raise an alert. */
const failing = (): Certificate => {
  return {
    ...certificateFor(SITE.domain, 'acme'),
    notAfter: '2099-01-01T10:00:00Z',
    lastRenewalAttemptAt: '2026-11-03T04:15:00Z',
    lastRenewalErrorCode: 'AcmeChallengeFailed',
    consecutiveRenewalFailures: 3,
    lastRenewalError: FAILURE_SENTENCE,
  }
}

/** A panel-issued certificate far from its end whose renewal has never failed. */
const healthy = (): Certificate => {
  return { ...certificateFor(SITE.domain, 'acme'), notAfter: '2099-01-01T10:00:00Z' }
}

/**
 * Installs the shell, the site and the certificate endpoints, then opens the SSL tab by clicking it.
 * @param page The Playwright page to drive.
 * @param installed The certificates the panel reports for the site.
 * @param options Who signs in and how the directory read answers.
 * @param options.user The signed-in user; the administrator when omitted.
 * @param options.directory The directory to report, `'failing'` for a refused read, or none.
 * @returns Resolves once the tab is on screen.
 */
const openSsl = async (
  page: Page,
  installed: Certificate[],
  options: { user?: AuthenticatedUser; directory?: AcmeDirectory | 'failing' } = {},
): Promise<void> => {
  await stubSignedIn(page, options.user)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubCertificates(page, installed)
  if (options.directory === 'failing') {
    await stubAcmeDirectoryFailure(page)
  } else if (options.directory !== undefined) {
    await stubAcmeDirectory(page, options.directory)
  }
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'SSL' }).click()
}

test('the card shows the last attempt, the failure sentence and the failure count', async ({ page }) => {
  await openSsl(page, [failing()])

  await expect(page.getByText('Last renewal attempt')).toBeVisible()
  const attempt = page.getByRole('definition').filter({ hasText: '2026' }).filter({ hasText: /:\d{2}/ })
  await expect(attempt).toBeVisible()
  await expect(page.getByText('Not tried yet')).toHaveCount(0)
  await expect(page.getByText(FAILURE_SENTENCE)).toBeVisible()
  await expect(page.getByText('Failed attempts in a row: 3', { exact: true })).toBeVisible()
})

test('the card shows no failure, no count and no attempt time when renewal has not failed', async ({ page }) => {
  await openSsl(page, [healthy()])

  // Positive control: the card is drawn, with the label the failing card also carries.
  await expect(page.getByText('Last renewal attempt')).toBeVisible()
  await expect(page.getByText('Not tried yet')).toBeVisible()
  await expect(page.getByText(FAILURE_SENTENCE)).toHaveCount(0)
  await expect(page.getByText(/Failed attempts in a row/)).toHaveCount(0)
})

test('an uploaded certificate has no renewal attempt row at all', async ({ page }) => {
  await openSsl(page, [{ ...certificateFor(SITE.domain, 'custom'), notAfter: '2099-01-01T10:00:00Z' }])

  // Positive control: the uploaded card is on screen.
  await expect(page.getByText('Uploaded')).toBeVisible()
  await expect(page.getByText('Last renewal attempt')).toHaveCount(0)
})

test('an administrator sees the directory kind and host', async ({ page }) => {
  await openSsl(page, [healthy()], { directory: DIRECTORY })

  await expect(page.getByText('Certificate authority')).toBeVisible()
  await expect(page.getByText('Staging (test certificates)')).toBeVisible()
  await expect(page.getByText(DIRECTORY.host, { exact: true })).toBeVisible()
})

test('a customer sees no directory line and the panel is never asked for it', async ({ page }) => {
  const asked: string[] = []
  page.on('request', (request) => {
    if (request.url().includes('/certificates/acme-directory')) {
      asked.push(request.url())
    }
  })
  await openSsl(page, [healthy()], { user: CUSTOMER, directory: DIRECTORY })

  // Positive control: the customer's certificate card is on screen, and so is its renewal row.
  await expect(page.getByText('Last renewal attempt')).toBeVisible()
  await expect(page.getByText('Certificate authority')).toHaveCount(0)
  await expect(page.getByText(DIRECTORY.host, { exact: true })).toHaveCount(0)
  expect(asked).toEqual([])
})

test('a failing directory read shows nothing and the tab still works', async ({ page }) => {
  const errors: string[] = []
  page.on('pageerror', (error) => {
    errors.push(error.message)
  })
  await openSsl(page, [healthy()], { directory: 'failing' })

  // Positive control: the certificate card loaded despite the failed read beside it.
  await expect(page.getByText('Last renewal attempt')).toBeVisible()
  await expect(page.getByText('Certificate authority')).toHaveCount(0)
  await expect(page.getByText('The directory read failed.')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Remove certificate' })).toBeEnabled()
  expect(errors).toEqual([])
})

test('the card chrome follows the interface language', async ({ page }) => {
  await setPersistedLocale(page, 'ru')
  await openSsl(page, [failing()], { directory: DIRECTORY })

  await expect(page.getByText('Последняя попытка обновления')).toBeVisible()
  await expect(page.getByText('Неудачных попыток подряд: 3', { exact: true })).toBeVisible()
  await expect(page.getByText('Центр сертификации')).toBeVisible()
  await expect(page.getByText(FAILURE_SENTENCE)).toBeVisible()
  await expect(page.getByText('Last renewal attempt')).toHaveCount(0)
})

test('the card chrome is drawn in Armenian', async ({ page }) => {
  await setPersistedLocale(page, 'hy')
  await openSsl(page, [healthy()], { directory: DIRECTORY })

  await expect(page.getByText('Թարմացման վերջին փորձը')).toBeVisible()
  await expect(page.getByText('Դեռ չի փորձվել')).toBeVisible()
  await expect(page.getByText('Վկայագրման կենտրոն')).toBeVisible()
  await expect(page.getByText('Certificate authority')).toHaveCount(0)
})

for (const theme of ['dark', 'light'] as const) {
  test(`the failing card and the directory line have no serious axe violation in the ${theme} theme`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: theme })
    await openSsl(page, [failing()], { directory: DIRECTORY })
    await expect(page.getByText('Failed attempts in a row: 3', { exact: true })).toBeVisible()

    await expectNoSeriousViolations(page)
  })
}
