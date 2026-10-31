import { answerBeforeDelay, earlySkeletons, recordSkeletonFlashes, runPastDelay } from '../fixtures/skeleton-flash'
import { expect, test, type Page } from '@playwright/test'
import { delayRoute } from '../fixtures/delay-route'
import { holdRoute } from '../fixtures/hold-route'
import {
  expectRegionFastLoad,
  expectRegionSlowFailure,
  expectRegionSlowLoad,
  type RegionLoadingScenario,
} from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  CERTIFICATE_SITE_ID,
  CERTIFICATES_PATTERN,
  certificateFor,
  stubCertificates,
  stubCertificatesProblem,
} from '../fixtures/stub-certificates-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubPhpVersions, stubSiteDetail, stubSites } from '../fixtures/stub-sites-routes'
import type { Certificate } from '../../src/types/certificate'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

// The site page while it is on the way (#243): the details card keeps its terms over placeholder
// values, and the SSL tab holds one certificate card with its real labels while the list loads.

const LICENSED_SITES: PanelModule[] = [{ name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true }]

const SITE: SiteDetail = {
  id: CERTIFICATE_SITE_ID,
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: [],
  backendType: 'static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: true,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * A certificate far from its end, so no "running out" notice lands above the card and moves it.
 */
const CERTIFICATE: Certificate = { ...certificateFor(SITE.domain, 'acme'), notAfter: '2030-01-01T00:00:00Z' }

/** The server's refusal the failure cases answer with. */
const REFUSAL = 'The server agent is not answering.'

/** The site read, and nothing else under `/sites/`. */
const SITE_PATTERN = `**/api/v1/sites/${SITE.id}`


/**
 * Signs in and stubs the shell and the site's neighbours.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubShell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubPhpVersions(page, [])
}

/** How the site detail screen is opened and answered. */
const SITE_DETAIL_SCENARIO: RegionLoadingScenario = {
  stub: async (page) => {
    await stubShell(page)
    await stubSiteDetail(page, SITE)
  },
  stubFailure: async (page) => {
    await stubShell(page)
    await page.route(SITE_PATTERN, async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'AgentUnavailable', detail: REFUSAL, status: 503 }),
      })
    })
  },
  pattern: SITE_PATTERN,
  path: `/sites/${SITE.id}`,
  anchor: (page) => {
    return page.getByRole('term').filter({ hasText: 'Document root' })
  },
  contentText: SITE.documentRoot,
  failureText: REFUSAL,
}

test('site detail: a slow answer shows placeholders under the real labels, then the content', async ({ page }) => {
  await expectRegionSlowLoad(page, SITE_DETAIL_SCENARIO)
})

test('site detail: a fast answer shows no placeholder', async ({ page }) => {
  await expectRegionFastLoad(page, SITE_DETAIL_SCENARIO)
})

test('site detail: a slow failure is an alert, never a skeleton', async ({ page }) => {
  await expectRegionSlowFailure(page, SITE_DETAIL_SCENARIO)
})

/**
 * Opens the site and then its SSL tab, whose certificate list is read when the tab mounts.
 * @param page The Playwright page.
 * @returns Resolves once the tab has been chosen.
 */
const openSsl = async (page: Page): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'SSL' }).click()
}

/**
 * The SSL tab's own region: the last one on the page, nested inside the site's.
 * @param page The Playwright page.
 * @returns The status line of the certificates region.
 */
const certificatesStatus = (page: Page): ReturnType<Page['getByTestId']> => {
  return page.getByTestId('loading-status').last()
}

test('ssl tab: a slow list holds a certificate card with its real labels, then the certificate', async ({ page }) => {
  await stubShell(page)
  await stubSiteDetail(page, SITE)
  await stubCertificates(page, [CERTIFICATE])
  await delayRoute(page, CERTIFICATES_PATTERN, 600)
  await openSsl(page)

  const anchor = page.getByText('Days left', { exact: true })
  await expect(anchor).toBeVisible()
  await expect(page.locator('[data-skeleton]').first()).toBeVisible()
  await expect(certificatesStatus(page)).toHaveText('Loading…')
  const before = await anchor.boundingBox()

  await expect(page.getByText('Issued by the panel')).toBeVisible()
  await expect(certificatesStatus(page)).toHaveText('Loaded')
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  expect(await anchor.boundingBox()).toEqual(before)
})

test('ssl tab: a fast list shows no placeholder', async ({ page }) => {
  await stubShell(page)
  await stubSiteDetail(page, SITE)
  await stubCertificates(page, [CERTIFICATE])
  const held = await holdRoute(page, CERTIFICATES_PATTERN)
  await recordSkeletonFlashes(page)
  await openSsl(page)

  // The page's clock is the test's (see `recordSkeletonFlashes`): the read is answered after 50 ms
  // of page time however busy the machine is, then time runs far past the 150 ms delay, and no
  // placeholder may have been seen at any point.
  await answerBeforeDelay(page, held)
  await expect(page.getByText('Issued by the panel')).toBeVisible()
  await runPastDelay(page)
  expect(await earlySkeletons(page)).toEqual([])
})

test('ssl tab: a slow refused list is an alert, never a skeleton', async ({ page }) => {
  await stubShell(page)
  await stubSiteDetail(page, SITE)
  await stubCertificates(page, [])
  await stubCertificatesProblem(page, REFUSAL)
  await delayRoute(page, CERTIFICATES_PATTERN, 600)
  await openSsl(page)

  await expect(page.getByText(REFUSAL)).toBeVisible()
  await expect(page.locator('[data-skeleton]')).toHaveCount(0)
  await expect(page.getByText('No certificate installed')).toHaveCount(0)
})
