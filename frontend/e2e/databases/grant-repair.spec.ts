import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  stubDatabaseGrants,
  stubDatabaseGrantsForbidden,
  stubDatabaseGrantsProblem,
} from '../fixtures/stub-database-grant-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import type { GrantRepairReport } from '../../src/types/grantRepair'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'databases', displayName: 'Databases', tier: 'included', isEnabled: true },
]

/**
 * A host with one repairable grant, one refused row belonging to nobody on this panel, and nine rows
 * that were already right. The refused row's two sentences are what the BACKEND would have sent —
 * the SPA holds no refusal vocabulary at all, which is the property several of these specs pin.
 */
const REPORT: GrantRepairReport = {
  isReportOnly: true,
  examinedGrants: 11,
  alreadyCorrect: 9,
  repaired: [],
  wouldRepair: [
    {
      databaseName: 'alice_shop',
      dbUsername: 'alice_shop',
      alsoMatchedDatabases: ['alicexshop'],
    },
  ],
  refused: [
    {
      grantHost: '10.0.0.5',
      databaseName: 'reporting_metrics',
      dbUsername: 'reporting_tool',
      reason: 'HostIsNotLocalhost',
      reasonDisplayName: 'Granted from another host',
      reasonAdvice:
        'Nothing was changed. Open your database server own grant table, then narrow the row by hand.',
    },
  ],
}

// The four buckets and the total are the whole of what makes this a census rather than a selection,
// and the operator decides on them. `toContainText` is case sensitive, and the figures are asserted
// with their labels rather than alone, because a page rendering "11" somewhere would otherwise pass.
test('the report shows the four buckets and says they account for every row read', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)

  await page.goto('/databases/grant-repair')

  // Positive control on the probe: the census card IS rendered, so a missing figure below is a fact
  // about the screen rather than about the locator. Asserted before the figures because the page
  // reads "Reading the server's grants…" until the request settles, and a spec that asserted over
  // that state would be measuring a spinner.
  await expect(page.getByRole('heading', { name: 'What the server holds' })).toBeVisible()

  // `exact: true` on each label, because `getByText` matches a case-insensitive SUBSTRING and the
  // paragraph below the figures quotes "the grants read" — a loose locator resolves to two elements
  // and fails strict mode, which is the honest failure but not the assertion wanted here.
  await expect(page.getByText('Grants read', { exact: true })).toBeVisible()
  await expect(page.getByText('Already correct', { exact: true })).toBeVisible()
  await expect(page.getByText('Would be narrowed', { exact: true })).toBeVisible()
  await expect(page.getByText('Left untouched', { exact: true })).toBeVisible()
  await expect(
    page.getByText('The four figures add up to the grants read', { exact: false }),
  ).toBeVisible()
  // The pass is named as an inspection, and the badge is the only thing on the page that says so.
  await expect(page.getByText('Inspection — nothing changed')).toBeVisible()
})

// The paragraph the threat note exists to put on a screen. It is asserted as a substring of the
// shipped sentence, and the second assertion is the one with teeth: the page must NOT say the host
// is clean, secure or unaffected.
test('the report states that a repair settles nothing about whether the reach was used', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)

  await page.goto('/databases/grant-repair')

  await expect(page.getByRole('heading', { name: 'What a repair would narrow' })).toBeVisible()
  await expect(
    page.getByText('That is what it could reach, not what it did.', { exact: false }),
  ).toBeVisible()
  await expect(
    page.getByText('does not establish whether anyone used it', { exact: false }),
  ).toBeVisible()
  // `getByText` matches a case-insensitive substring, so these cover "Secure", "clean" and the rest.
  await expect(page.getByText('your server is secure')).toHaveCount(0)
  await expect(page.getByText('no longer at risk')).toHaveCount(0)
  await expect(page.getByText('nothing was exposed')).toHaveCount(0)
})

// A row with an empty exposure list is the case a screen is most likely to get wrong: an empty list
// reads as an all-clear, and it is not one. The sentence must be there, and it must not be a blank.
test('a repaired row with nothing else matching says so without reading as an all clear', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, {
    ...REPORT,
    wouldRepair: [
      { databaseName: 'alice_shop', dbUsername: 'alice_shop', alsoMatchedDatabases: [] },
    ],
  })

  await page.goto('/databases/grant-repair')

  await expect(page.getByText('Also reachable with this grant')).toBeVisible()
  await expect(
    page.getByText('No other database on the server matches it right now.', { exact: false }),
  ).toBeVisible()
  await expect(
    page.getByText('An empty list does not clear a row either', { exact: false }),
  ).toBeVisible()
})

// A refused row is work handed back to the operator, so it must carry the server's raw columns, the
// backend's sentence for the verdict, and the backend's sentence for what to do. The escapes and the
// host are asserted verbatim: this row is refused precisely because the panel did not write it.
test('a refused row shows the raw columns, the verdict and what to do about it', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)

  await page.goto('/databases/grant-repair')

  await expect(page.getByRole('heading', { name: 'Left exactly as found' })).toBeVisible()
  await expect(page.getByText('Granted from another host')).toBeVisible()
  await expect(page.getByText('10.0.0.5')).toBeVisible()
  await expect(page.getByText('reporting_metrics')).toBeVisible()
  await expect(page.getByText('reporting_tool')).toBeVisible()
  await expect(page.getByText('then narrow the row by hand', { exact: false })).toBeVisible()
  // And the screen says whose names those are, because a reader shown an unfamiliar database name
  // will otherwise assume the panel has lost one of theirs.
  await expect(
    page.getByText('the names above are frequently somebody else', { exact: false }),
  ).toBeVisible()
})

// The gate, end to end. The repair is offered only over a held inspection, it confirms in a dialog
// naming what it will rewrite, and the answer replaces the plan with the outcome.
test('the repair is confirmed in a dialog and replaces the plan with what it changed', async ({
  page,
}) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)

  await page.goto('/databases/grant-repair')

  await expect(page.getByRole('heading', { name: 'Run the repair' })).toBeVisible()
  await page.getByRole('button', { name: 'Narrow the grants listed' }).click()

  // The dialog names the number of grants, so the operator is confirming the figure they just read.
  // Asserted as the SINGULAR, and paired with the plural case below: the phrase is pluralised through
  // `pluralRules`, and `1 grant(s)` — what this read before — is the machine form that rule replaced.
  await expect(page.getByRole('dialog')).toContainText('1 grant on this server')
  await page.getByRole('button', { name: 'Narrow them' }).click()

  // The outcome, not the plan: the badge and the heading both change, and "would be narrowed" is
  // gone. Without the last assertion a screen that showed both lists would pass.
  await expect(page.getByText('Repair performed')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'What the repair narrowed' })).toBeVisible()
  await expect(page.getByText('Would be narrowed')).toHaveCount(0)
  await expect(page.getByText('Narrowed', { exact: true })).toBeVisible()
})

// The plural half of the phrase above, and the reason it is a separate test rather than a second
// assertion: a message pluralised for one count proves nothing about the rule, because a rule that
// always answered "first form" passes the singular case. Two counts that must differ is the smallest
// pair that can observe the choice at all (rules/testing.md).
//
// UNOBSERVED HERE: russian's and armenian's forms. This suite runs the panel in english, which takes
// two forms and would be served by vue-i18n's default rule; russian's three-form rule and armenian's
// invariant one are exercised by neither. They were verified in a live browser against a real host at
// 1, 2, 11 and 21 grants — 21 being the case that separates the rule from a `count === 1` test — and
// that verification lives in a session log, not here. Naming the gap is the honest half of the check.
test('the dialog counts more than one grant in the plural', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, {
    ...REPORT,
    wouldRepair: [
      {
        databaseName: 'alice_shop',
        dbUsername: 'alice_shop',
        alsoMatchedDatabases: ['alicexshop'],
      },
      {
        databaseName: 'bob_blog',
        dbUsername: 'bob_blog',
        alsoMatchedDatabases: ['bobxblog'],
      },
    ],
  })

  await page.goto('/databases/grant-repair')

  await page.getByRole('button', { name: 'Narrow the grants listed' }).click()

  await expect(page.getByRole('dialog')).toContainText('2 grants on this server')
  // The control: the singular must NOT be what a two-row plan renders. Without this a message that
  // ignored the count entirely and always said "grant" would satisfy the line above by substring.
  await expect(page.getByRole('dialog')).not.toContainText('2 grant on this server')
})

// The inverse of the gate: once the repair has run, the same census comes back as an ACTION and the
// control must be gone. Offering it again would invite a second host-wide rewrite over a census
// nobody had read as a plan.
test('the repair is not offered again over the result of one', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)

  await page.goto('/databases/grant-repair')
  await page.getByRole('button', { name: 'Narrow the grants listed' }).click()
  await page.getByRole('button', { name: 'Narrow them' }).click()

  await expect(page.getByText('Repair performed')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Narrow the grants listed' })).toHaveCount(0)
  await expect(
    page.getByText('The repair is offered on a fresh reading', { exact: false }),
  ).toBeVisible()
})

// A host with nothing to repair is an ANSWER about the server, not an empty screen and not a
// failure: the census still reads, the list says there is nothing to narrow, and the button is not
// offered as something to press.
test('a host with nothing to narrow says so instead of offering a repair', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, {
    isReportOnly: true,
    examinedGrants: 9,
    alreadyCorrect: 9,
    repaired: [],
    wouldRepair: [],
    refused: [],
  })

  await page.goto('/databases/grant-repair')

  await expect(page.getByRole('heading', { name: 'What the server holds' })).toBeVisible()
  await expect(page.getByText('Nothing to narrow')).toBeVisible()
  await expect(page.getByText('Nothing was left untouched')).toBeVisible()
  // `status`, not `alert`: `UiAlert` renders `role="status"`, so a `getByRole('alert')` assertion
  // here would have been vacuous — it matches nothing on this page whether or not a banner is drawn.
  // It is discriminating rather than decorative, and that is measured: the failure case at the bottom
  // of this file asserts the same locator CONTAINS the backend's sentence, so a zero here is a fact
  // about the screen. `UiSpinner` shares the role and has already gone by this point.
  await expect(page.getByRole('status')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Narrow the grants listed' })).toBeDisabled()
})

// A customer who types the URL is refused by the endpoint, and the screen draws that as
// non-disclosure rather than as a broken server. The second half is the security half: none of the
// other tenants' names the refused body carried may reach the page.
test('a caller the panel refuses is told who may run this and shown no census', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrantsForbidden(page)

  await page.goto('/databases/grant-repair')

  await expect(page.getByText('Not shown to you')).toBeVisible()
  await expect(page.getByText('only an administrator may open it', { exact: false })).toBeVisible()
  await expect(page.getByRole('status')).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'What the server holds' })).toHaveCount(0)
  await expect(page.getByText('reporting_metrics')).toHaveCount(0)
})

// A real failure is a different state again, and its text is the backend's: the SPA holds no locale
// key for a server message (rules/vue.md), so the sentence on screen must be the one that arrived.
test('a failure renders the backend own message and not a census', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrantsProblem(page, 'The server agent could not be reached.')

  await page.goto('/databases/grant-repair')

  await expect(page.getByRole('status')).toContainText('The server agent could not be reached.')
  await expect(page.getByRole('heading', { name: 'What the server holds' })).toHaveCount(0)
})

// The screen has to be reachable. A maintenance page nobody can find is a repair that still only
// runs if somebody calls the endpoint by hand, which is the gap this whole lane exists to close.
test('the databases list links to the grant repair screen', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubDatabaseGrants(page, REPORT)
  await page.route('**/api/v1/databases', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })
  await page.route('**/api/v1/accounts*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.goto('/databases')

  const link = page.getByRole('link', { name: 'Grant repair' })
  await expect(link).toBeVisible()
  await link.click()

  await expect(page.getByRole('heading', { name: 'Grant repair' })).toBeVisible()
})
