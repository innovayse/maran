import type { Page, Route } from '@playwright/test'
import type { CronEntry, CronEntryOutput } from '../../src/types/cronEntry'
import type { CronEnvironmentVariable } from '../../src/types/cronEnvironmentVariable'

/**
 * The Cron module's routes are matched with regular expressions rather than globs, because every
 * one of its reads carries `?accountId=` — the account is what the module's contract names on every
 * call, since cron keeps no rows and an entry id means nothing until it is asked of one account's
 * crontab. A glob ending at the path would match none of them.
 *
 * Registration order matters: Playwright gives priority to the most recently registered route, so
 * the narrow patterns are installed after the collection they sit under.
 */

/** The collection endpoint the entries list and the create call both talk to. */
const ENTRIES_PATTERN = /\/api\/v1\/cron-entries(\?.*)?$/

/** The single-entry endpoint, which answers the rewrite and the removal. */
const ENTRY_PATTERN = /\/api\/v1\/cron-entries\/[^/?]+(\?.*)?$/

/** The switch endpoint. */
const ENABLED_PATTERN = /\/api\/v1\/cron-entries\/[^/?]+\/enabled(\?.*)?$/

/** The last-run endpoint. */
const OUTPUT_PATTERN = /\/api\/v1\/cron-entries\/[^/?]+\/output(\?.*)?$/

/** The crontab preamble endpoint, read and replaced whole. */
const ENVIRONMENT_PATTERN = /\/api\/v1\/cron-environment(\?.*)?$/

/**
 * Fulfils the entries collection: `GET` reports the list, `POST` echoes the submitted entry back as
 * installed, with an identifier of the shape the agent mints.
 * @param page The Playwright page whose network the route is installed on.
 * @param entries The entries the stub reports; a create is pushed onto it.
 * @returns Resolves once the route is installed.
 */
export const stubCronEntries = async (page: Page, entries: CronEntry[]): Promise<void> => {
  await page.route(ENTRIES_PATTERN, async (route: Route) => {
    if (route.request().method() === 'POST') {
      const submitted = route.request().postDataJSON() as Pick<
        CronEntry,
        'accountId' | 'schedule' | 'command'
      >
      const created: CronEntry = {
        entryId: '99999999-9999-9999-9999-999999999999',
        accountId: submitted.accountId,
        schedule: submitted.schedule,
        command: submitted.command,
        enabled: true,
      }
      entries.push(created)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(entries),
    })
  })
}

/**
 * Fulfils the entries collection with an RFC 7807 problem body, so a spec can assert the screen
 * renders the panel's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubCronEntriesProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(ENTRIES_PATTERN, async (route: Route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'CronAccountNotFound', title: 'Not found', detail }),
    })
  })
}

/**
 * Fulfils the single-entry endpoint: the rewrite and the removal both answer `true`, which is what
 * the module's `Result<bool>` translates to.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubCronEntryMutations = async (page: Page): Promise<void> => {
  await page.route(ENTRY_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

/**
 * Fulfils the switch endpoint.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubCronEntryEnabled = async (page: Page): Promise<void> => {
  await page.route(ENABLED_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
  })
}

/**
 * Fulfils the last-run endpoint.
 *
 * `null` is a real answer here and is served as the literal JSON `null` with a 200, which is
 * exactly what `GetCronEntryOutputQueryHandler` produces for an entry that has never run — the
 * handler returns `Result<CronEntryOutputDto?>` and `ToActionResult` wraps the null value in an
 * `OkObjectResult`. Serving `204` or `{}` instead would be the stub agreeing with a belief the SPA
 * might hold rather than with the module.
 * @param page The Playwright page whose network the route is installed on.
 * @param output The reading, or `null` for an entry that has never run.
 * @returns Resolves once the route is installed.
 */
export const stubCronEntryOutput = async (
  page: Page,
  output: CronEntryOutput | null,
): Promise<void> => {
  await page.route(OUTPUT_PATTERN, async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(output),
    })
  })
}

/**
 * Fulfils the crontab preamble endpoint: `GET` reports the set, `PUT` accepts a replacement.
 * @param page The Playwright page whose network the route is installed on.
 * @param variables The assignments the stub reports.
 * @returns Resolves once the route is installed.
 */
export const stubCronEnvironment = async (
  page: Page,
  variables: CronEnvironmentVariable[],
): Promise<void> => {
  await page.route(ENVIRONMENT_PATTERN, async (route: Route) => {
    if (route.request().method() === 'PUT') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(variables),
    })
  })
}
