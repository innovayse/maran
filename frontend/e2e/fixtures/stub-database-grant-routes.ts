import type { Page } from '@playwright/test'
import type { GrantRepairReport } from '../../src/types/grantRepair'

/** The report endpoint the grant-repair screen reads on mount. */
const GRANTS_PATTERN = '**/api/v1/database-grants'

/** The repair endpoint. A `*` never spans a `/`, so this is the narrower pattern. */
const REPAIR_PATTERN = '**/api/v1/database-grants/repair'

/**
 * Fulfils `GET /api/v1/database-grants` with a census, and `POST .../repair` with the census the
 * same host would report after acting.
 *
 * The repaired answer is DERIVED from the report rather than supplied separately: the rows move
 * from `wouldRepair` to `repaired` and `isReportOnly` flips, which is exactly what the server does.
 * A stub that answered the repair with an independently written body would let a screen that showed
 * the plan instead of the outcome pass.
 * @param page The Playwright page whose network the routes are installed on.
 * @param report The census the report request answers.
 * @returns Resolves once both routes are installed.
 */
export const stubDatabaseGrants = async (
  page: Page,
  report: GrantRepairReport,
): Promise<void> => {
  await page.route(GRANTS_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(report),
    })
  })

  await page.route(REPAIR_PATTERN, async (route) => {
    const submitted = route.request().postDataJSON() as { expectedRepairCount: number }

    // The server's own gate, mirrored: a figure that does not match what this host would rewrite is
    // answered 409 and nothing is changed. Mirrored rather than always accepted, because the
    // screen's promise is that the repair runs on the list that was read and on no other.
    if (submitted.expectedRepairCount !== report.wouldRepair.length) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          code: 'DatabaseGrantRepairPlanChanged',
          title: 'Conflict',
          detail: "The server's grants no longer match the report you read.",
        }),
      })
      return
    }

    const acted: GrantRepairReport = {
      ...report,
      isReportOnly: false,
      repaired: report.wouldRepair,
      wouldRepair: [],
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(acted),
    })
  })
}

/**
 * Refuses `GET /api/v1/database-grants` with a 403 carrying a body, so a spec can assert the screen
 * draws non-disclosure rather than a failure — and that it invents no census of its own.
 *
 * The body is deliberately NOT empty: a refusal that carried nothing would make a "no other
 * tenant's names on screen" assertion vacuous, because there would have been nothing to leak.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubDatabaseGrantsForbidden = async (page: Page): Promise<void> => {
  await page.route(GRANTS_PATTERN, async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'Forbidden',
        title: 'Forbidden',
        detail: 'This caller may not read the host grant table.',
      }),
    })
  })
}

/**
 * Fulfils `GET /api/v1/database-grants` with an RFC 7807 problem body, so a spec can assert the page
 * renders the backend's own already-localized message verbatim.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubDatabaseGrantsProblem = async (page: Page, detail: string): Promise<void> => {
  await page.route(GRANTS_PATTERN, async (route) => {
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AgentSystemFailure', title: 'Unexpected error', detail }),
    })
  })
}
