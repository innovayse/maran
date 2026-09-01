import type { Page } from '@playwright/test'
import type { Certificate } from '../../src/types/certificate'

/** The collection endpoint the SSL tab lists, issues and installs through. */
const CERTIFICATES_PATTERN = '**/api/v1/certificates'

/** The custom-install endpoint. */
const CUSTOM_PATTERN = '**/api/v1/certificates/custom'

/** The single-certificate endpoint, which answers the removal. */
const CERTIFICATE_PATTERN = '**/api/v1/certificates/*'

/**
 * Fulfils the certificate endpoints against an in-memory list: `GET` lists it, `POST` and
 * `POST /custom` add to it and echo what they added, and `DELETE` removes by id.
 *
 * The list is the stub's own state and is mutated by the calls, so a spec can drive the tab
 * through issue → list → remove and watch the screen follow — which is the behaviour that
 * matters and the one the tab's previous specs could not reach, because the API composable
 * rejected every call and every one of those specs asserted the rejection.
 * @param page The Playwright page whose network the routes are installed on.
 * @param installed The certificates the stub starts with; it is mutated in place.
 * @returns Resolves once every route is installed.
 */
export const stubCertificates = async (page: Page, installed: Certificate[]): Promise<void> => {
  await page.route(CERTIFICATES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as { domain: string }
      const issued = certificateFor(submitted.domain, 'acme')
      installed.push(issued)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(issued),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(installed),
    })
  })

  // Installed AFTER the collection pattern, because Playwright gives priority to the most
  // recently registered route and these two are the narrower ones.
  await page.route(CUSTOM_PATTERN, async (route) => {
    const submitted = route.request().postDataJSON() as { domain: string }
    const uploaded = certificateFor(submitted.domain, 'custom')
    installed.push(uploaded)
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(uploaded),
    })
  })

  await page.route(CERTIFICATE_PATTERN, async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }

    const id = new URL(route.request().url()).pathname.split('/').pop() ?? ''
    const at = installed.findIndex((certificate) => {
      return certificate.id === id
    })
    if (at >= 0) {
      installed.splice(at, 1)
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

/**
 * Fulfils `GET /api/v1/certificates` with an RFC 7807 problem body, so a spec can assert the tab
 * renders the backend's own already-localized message verbatim rather than copy of its own.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCertificatesProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(CERTIFICATES_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * The site every certificate fixture here belongs to.
 *
 * Exported so a spec's site fixture and its certificate fixture agree without either restating
 * the other's id: the tab lists by site, so a mismatch would show as an empty list and read as a
 * defect in the screen.
 */
export const CERTIFICATE_SITE_ID = '11111111-1111-1111-1111-111111111111'

/**
 * Builds one certificate in the shape `CertificateDto` serializes to.
 * @param domain The domain it covers.
 * @param source Whether the panel issued it or the customer supplied it.
 * @returns The certificate.
 */
export const certificateFor = (
  domain: string,
  source: Certificate['source'],
): Certificate => {
  return {
    id: `${source}-certificate`,
    siteId: CERTIFICATE_SITE_ID,
    domain,
    source,
    issuedAt: '2026-08-01T10:00:00Z',
    notAfter: '2026-11-01T10:00:00Z',
    lastRenewalAttemptAt: null,
    lastRenewalErrorCode: '',
    consecutiveRenewalFailures: 0,
  }
}
