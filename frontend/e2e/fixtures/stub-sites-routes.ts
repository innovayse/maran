import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { InstallablePhpVersion, PhpVersion } from '../../src/types/phpVersion'
import type { Site, SiteChoices, SiteDetail, SiteLimits } from '../../src/types/site'

/** The collection endpoint the sites list and the create form both talk to, with or without a query. */
const SITES_PATTERN = /\/api\/v1\/sites(\?.*)?$/

/** The single-site endpoint, which also answers the enable, disable and delete calls. */
const SITE_PATTERN = '**/api/v1/sites/*'

/** The installed-PHP endpoint the site form's runtime picker reads. */
const PHP_VERSIONS_PATTERN = '**/api/v1/sites/php-versions'

/** The installable-PHP endpoint the operator's install picker reads. */
const INSTALLABLE_PHP_VERSIONS_PATTERN = '**/api/v1/sites/php-versions/installable'

/** The limits endpoint the site form reads its alias bound from. */
const SITE_LIMITS_PATTERN = '**/api/v1/sites/limits'

/** The choices endpoint the site form and the list's filters read the backends and states from. */
const SITE_CHOICES_PATTERN = '**/api/v1/sites/choices'

/** The sub-paths of the collection that are not a site's id, so the single-site stub leaves them alone. */
const COLLECTION_READS = /\/api\/v1\/sites\/(choices|limits|php-versions|php-versions\/installable)$/

/** The log-tail endpoint. It does not exist on the backend yet; the contract is Task 16's. */
const SITE_LOGS_PATTERN = '**/api/v1/sites/*/logs*'

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
 * Fulfils `GET /api/v1/sites/php-versions/installable` with the versions the host can install.
 * @param page The Playwright page whose network the route is installed on.
 * @param read Returns the versions to answer with; called per request so a spec can change the answer.
 * @returns Resolves once the route is installed.
 */
export const stubInstallablePhpVersions = async (
  page: Page,
  read: () => InstallablePhpVersion[],
): Promise<void> => {
  await page.route(INSTALLABLE_PHP_VERSIONS_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(read()) })
  })
}

/**
 * Answers `GET /api/v1/sites/limits` with the given limits, or with a 500 when `limits` is null.
 * @param page The Playwright page whose network the route is installed on.
 * @param limits The limits the stub reports, or null for a server that cannot answer.
 * @returns Resolves once the route is installed.
 */
export const stubSiteLimits = async (page: Page, limits: SiteLimits | null): Promise<void> => {
  await page.route(SITE_LIMITS_PATTERN, async (route) => {
    if (limits === null) {
      await route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{}' })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(limits) })
  })
}

/** The choices the panel serves by default, labelled as it labels them in English. */
export const DEFAULT_SITE_CHOICES: SiteChoices = {
  backendTypes: [
    { value: 'static', label: 'Static' },
    { value: 'php', label: 'PHP' },
    { value: 'reverseProxy', label: 'Reverse proxy' },
  ],
  statuses: [
    { value: 'enabled', label: 'Enabled' },
    { value: 'disabled', label: 'Disabled' },
  ],
}

/**
 * Answers `GET /api/v1/sites/choices` with the given backends and states. Installed again after
 * {@link stubSites} (which installs the default answer) it replaces that answer, because Playwright
 * gives priority to the most recently registered route.
 * @param page The Playwright page whose network the route is installed on.
 * @param choices The choices the stub reports; the panel's English defaults when omitted.
 * @returns Resolves once the route is installed.
 */
export const stubSiteChoices = async (page: Page, choices: SiteChoices = DEFAULT_SITE_CHOICES): Promise<void> => {
  await page.route(SITE_CHOICES_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(choices) })
  })
}

/** How the sites endpoint searches, sorts and filters, as the panel does. */
const SITE_RULES: PagedRules<Site> = {
  matches: (site, term) => {
    return site.domain.toLowerCase().includes(term) || site.ownerName.toLowerCase().includes(term)
  },
  sorts: {
    domain: (site) => {
      return site.domain
    },
    status: (site) => {
      return site.status
    },
    createdAt: (site) => {
      return site.createdAt
    },
  },
  filters: {
    accountId: (site, value) => {
      return site.accountId === value
    },
    backendType: (site, value) => {
      return site.backendType === value
    },
    phpVersion: (site, value) => {
      return site.phpVersion === value
    },
    status: (site, value) => {
      return site.status === value
    },
  },
}

/**
 * Fulfils `GET /api/v1/sites` as a page honouring `page`, `pageSize`, `search`, `sort`,
 * `accountId`, `backendType`, `phpVersion` and `status`, and answers a `POST` by echoing the submitted
 * body back as a created site, which joins the catalogue. It does not answer the installed PHP
 * versions: the list's filter reads them, so a spec opening the list installs
 * {@link stubPhpVersions} itself, and no spec's versions depend on the order of two stubs.
 * @param page The Playwright page whose network the route is installed on.
 * @param sites The sites the stub knows, in their default order.
 * @returns A function reporting every list request so far, oldest first, so a spec can read what
 * the page asked the panel for.
 */
export const stubSites = async (page: Page, sites: Site[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  // The pickers read the backends and states from the panel, so every spec opening the form or the
  // list needs an answer; a spec that wants different ones installs stubSiteChoices again.
  await stubSiteChoices(page)

  await page.route(SITES_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as Pick<
        Site,
        'accountId' | 'domain' | 'backendType' | 'phpVersion'
      >
      const created: Site = {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        ownerName: '',
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

    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(sites, route.request().url(), SITE_RULES)),
    })
  })

  return () => {
    return listRequests
  }
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
 * Serves an empty `GET /api/v1/sites` page but rejects `POST` with an RFC 7807 problem body, so a
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
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
    })
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
    if (COLLECTION_READS.test(new URL(route.request().url()).pathname)) {
      // `choices`, `limits` and `php-versions` are collection reads, not a site's id.
      await route.fallback()
      return
    }
    if (route.request().method() === 'DELETE') {
      await route.fulfill({ status: 204, body: '' })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail) })
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
