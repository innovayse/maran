import type { Page } from '@playwright/test'
import type { PhpVersion } from '../../src/types/phpVersion'
import type { Site, SiteDetail } from '../../src/types/site'

/** The collection endpoint the sites list and the create form both talk to. */
const SITES_PATTERN = '**/api/v1/sites'

/** The single-site endpoint, which also answers the enable, disable and delete calls. */
const SITE_PATTERN = '**/api/v1/sites/*'

/** The installed-PHP endpoint the site form's runtime picker reads. */
const PHP_VERSIONS_PATTERN = '**/api/v1/sites/php-versions'

/** The log-tail endpoint. It does not exist on the backend yet; the contract is Task 16's. */
const SITE_LOGS_PATTERN = '**/api/v1/sites/*/logs*'

/**
 * Fulfils `GET /api/v1/sites` with the given list and answers a `POST` by echoing the submitted
 * body back as a created site, so a spec can drive the list page's states and the form's success
 * path without a live backend.
 * @param page The Playwright page whose network the route is installed on.
 * @param sites The sites the stub reports for the list request.
 * @returns Resolves once the route is installed.
 */
export const stubSites = async (page: Page, sites: Site[]): Promise<void> => {
  await page.route(SITES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as Pick<
        Site,
        'accountId' | 'domain' | 'backendType' | 'phpVersion'
      >
      const created: Site = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        domain: submitted.domain,
        backendType: submitted.backendType,
        phpVersion: submitted.phpVersion,
        status: 'enabled',
        createdAt: '2026-08-30T10:00:00Z',
      }
      sites.push(created)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(sites) })
  })
}

/**
 * Fulfils `GET /api/v1/sites` with an RFC 7807 problem body, so a spec can assert the list page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubSitesProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SITES_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/sites` list but rejects `POST` with an RFC 7807 problem body, so a
 * spec can assert the create form surfaces the server's own message rather than invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCreateSiteProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(SITES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'SiteDomainTaken', title: 'Conflict', detail }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
}

/**
 * Fulfils the single-site endpoint: `GET` returns the detail, `DELETE` reports success, and the
 * `POST` sub-routes (enable, disable, php-version) return a list-shaped site.
 *
 * Installed AFTER {@link stubSites} in a spec, because Playwright gives priority to the most
 * recently registered route and this pattern is the narrower one.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The site the stub reports.
 * @returns Resolves once the route is installed.
 */
export const stubSiteDetail = async (page: Page, detail: SiteDetail): Promise<void> => {
  await page.route(SITE_PATTERN, async (route) => {
    if (route.request().method() === 'DELETE') {
      await route.fulfill({ status: 204, body: '' })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail) })
  })
}

/**
 * Fulfils `GET /api/v1/sites/php-versions` with the given runtimes.
 * @param page The Playwright page whose network the route is installed on.
 * @param versions The versions the stub reports as installed.
 * @returns Resolves once the route is installed.
 */
export const stubPhpVersions = async (page: Page, versions: PhpVersion[]): Promise<void> => {
  await page.route(PHP_VERSIONS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(versions) })
  })
}

/**
 * Fulfils the site-log SSE endpoint with a fixed body.
 *
 * The backend has no logs route yet — Task 16 documented the contract this SPA reads, and this
 * stub speaks exactly that contract rather than a backend being invented at the network layer.
 * The whole body is delivered at once, which is all these specs need: what is under test is how
 * the pane REPORTS an ending, not how the decoder handles chunk boundaries (Task 16's own store
 * specs cover that).
 * @param page The Playwright page whose network the route is installed on.
 * @param body The raw `text/event-stream` body to serve.
 * @returns Resolves once the route is installed.
 */
export const stubSiteLogStream = async (page: Page, body: string): Promise<void> => {
  await page.route(SITE_LOGS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body })
  })
}

/**
 * Builds one `line` frame of the log stream's SSE body.
 * @param line The raw log line, exactly as a customer's request would have produced it.
 * @param historical Whether the line came from the replayed tail rather than live.
 * @returns The frame, terminated by the blank line SSE requires.
 */
export const logLineFrame = (line: string, historical = false): string => {
  return `event: line\ndata: ${JSON.stringify({ line, historical })}\n\n`
}

/**
 * Builds the terminal `end` frame of the log stream's SSE body.
 * @param reason The ending the panel names.
 * @returns The frame, terminated by the blank line SSE requires.
 */
export const logEndFrame = (reason: string): string => {
  return `event: end\ndata: ${JSON.stringify({ reason })}\n\n`
}

/**
 * Installs a log-stream route that accepts the request and never answers it, so a spec can
 * observe what the panel does with a stream that is still open.
 *
 * The route handler never fulfils. That is the point: the only way this request ever finishes is
 * the panel aborting it, which is exactly the teardown under test. The request is abandoned when
 * the page closes at the end of the spec, so nothing here can outlive it.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubOpenSiteLogStream = async (page: Page): Promise<void> => {
  await page.route(SITE_LOGS_PATTERN, async () => {
    await new Promise(() => {
      // Deliberately never resolved.
    })
  })
}
