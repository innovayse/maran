import type { Page } from '@playwright/test'
import type { ReplaceSiteOptionsRequest, SiteOptions } from '../../src/types/siteOptions'

/** The id of the site the options specs open; every options route is built from it. */
export const OPTIONS_SITE_ID = '33333333-3333-3333-3333-333333333333'

/** Where the options of that site are read and replaced. */
export const SITE_OPTIONS_PATTERN = `**/api/v1/sites/${OPTIONS_SITE_ID}/options`

/**
 * A site with nothing set, with the bounds and choices the panel serves, labelled as it labels them
 * in English. The server owns these lists; the specs hold a copy only to answer with.
 */
export const EMPTY_SITE_OPTIONS: SiteOptions = {
  siteId: OPTIONS_SITE_ID,
  canonicalHost: 'none',
  clientMaxBodySizeMib: null,
  staticCacheDays: null,
  redirects: [],
  basicAuthUsers: [],
  ipAccessMode: null,
  ipRules: [],
  requestRate: null,
  limits: {
    maximumRedirects: 25,
    maximumBasicAuthUsers: 10,
    maximumIpRules: 25,
    minimumBodySizeMib: 1,
    maximumBodySizeMib: 1024,
    planMaximumBodySizeMib: null,
    minimumCacheDays: 1,
    maximumCacheDays: 365,
    minimumPasswordLength: 8,
    minimumRequestsPerSecond: 1,
    maximumRequestsPerSecond: 1000,
    minimumRequestBurst: 0,
    maximumRequestBurst: 1000,
  },
  choices: {
    canonicalHosts: [
      { value: 'none', label: 'No main name' },
      { value: 'www', label: 'Use the www name' },
      { value: 'apex', label: 'Use the name without www' },
    ],
    redirectMatches: [
      { value: 'exact', label: 'This exact path' },
      { value: 'prefix', label: 'This folder and everything in it' },
    ],
    redirectStatuses: [
      { value: 'permanent', label: 'Permanent (301)' },
      { value: 'temporary', label: 'Temporary (302)' },
    ],
    ipAccessModes: [
      { value: 'allowList', label: 'Allow only these addresses' },
      { value: 'denyList', label: 'Block these addresses' },
    ],
  },
}

/** What a stubbed options endpoint keeps and reports, so a spec can read what the page sent. */
export interface SiteOptionsStub {
  /** Every body the page sent with `PUT`, oldest first. */
  readonly saved: readonly ReplaceSiteOptionsRequest[]
}

/**
 * Answers the options endpoint: `GET` with the held options, and `PUT` by storing the body the way the
 * panel does (users become names only, a rule list with no entries has no mode) and answering with it.
 * @param page The Playwright page whose network the route is installed on.
 * @param initial The options the panel starts with.
 * @returns What the page sent, readable by the spec.
 */
export const stubSiteOptions = async (page: Page, initial: SiteOptions = EMPTY_SITE_OPTIONS): Promise<SiteOptionsStub> => {
  let held = initial
  const saved: ReplaceSiteOptionsRequest[] = []

  await page.route(SITE_OPTIONS_PATTERN, async (route) => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON() as ReplaceSiteOptionsRequest
      saved.push(body)
      held = {
        ...held,
        canonicalHost: body.canonicalHost,
        clientMaxBodySizeMib: body.clientMaxBodySizeMib,
        staticCacheDays: body.staticCacheDays,
        redirects: body.redirects,
        basicAuthUsers: body.basicAuthUsers.map((user) => {
          return user.name
        }),
        ipAccessMode: body.ipAccessMode,
        ipRules: body.ipRules,
        requestRate: body.requestRate,
      }
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) })
  })

  return { saved }
}

/**
 * Refuses every `PUT` to the options endpoint with a problem the way the panel words one, and answers
 * `GET` with the given options.
 * @param page The Playwright page whose network the route is installed on.
 * @param code The machine code of the refusal; it decides which section shows the message.
 * @param message The localized sentence the panel sends.
 * @param initial The options the panel starts with.
 * @returns Resolves once the route is installed.
 */
export const stubSiteOptionsRefusal = async (
  page: Page,
  code: string,
  message: string,
  initial: SiteOptions = EMPTY_SITE_OPTIONS,
): Promise<void> => {
  await page.route(SITE_OPTIONS_PATTERN, async (route) => {
    if (route.request().method() === 'PUT') {
      await route.fulfill({
        status: 400,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code, title: message, detail: message, status: 400 }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(initial) })
  })
}
