import { expect, type Page, type Route } from '@playwright/test'
import { MIGRATION_MODULES } from './migration'
import { accountStateName, jobStateName } from './migration-state-names'
import { mainNavigation, openSection } from './open-full-shell'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubLandingReads } from './stub-landing-reads'
import { stubModules } from './stub-modules-route'
import { stubShellReads } from './stub-shell-reads'
import type { MigrationJob, MigrationJobAccount, MigrationJobReport } from '../../src/types/migrationJob'

// The panel's migration-job endpoints, stubbed (issue #197, phase 2). The stub holds the state, so a spec reads
// back what the page sent; a request no stub answers fails the guarded test.

/** The job the stub holds. */
export const JOB_ID = '30000000-0000-4000-8000-000000000001'

/** A draft job of two accounts. */
export const DRAFT_JOB: MigrationJob = {
  id: JOB_ID,
  remoteServerId: '20000000-0000-4000-8000-000000000001',
  name: 'Move the old server',
  state: 'Draft',
  stateName: jobStateName('Draft'),
  concurrency: 2,
  bandwidthLimitKib: null,
  sourceUrl: null,
  accountCount: 2,
  accountStates: { Pending: 2 },
  createdAt: '2026-11-09T10:00:00Z',
  startedAt: null,
  finishedAt: null,
}

/**
 * One account of the job.
 * @param name The account's name on the source.
 * @param fields What differs from a pending, unmapped account.
 * @returns The account.
 */
export const jobAccount = (name: string, fields: Partial<MigrationJobAccount> = {}): MigrationJobAccount => {
  return {
    id: `4000000${name.length}-0000-4000-8000-00000000000${name.charCodeAt(0) % 10}`,
    sourceAccountName: name,
    sourceHome: `/home/${name}`,
    sourceDiskBytes: 1024,
    siteCount: 1,
    databaseCount: 1,
    cronEntryCount: 0,
    mailboxCount: 0,
    planId: null,
    resellerId: null,
    ownerEmail: null,
    ownerLanguage: 'en',
    primaryDomain: `${name}.example.test`,
    hasPullGrant: false,
    state: 'Pending',
    stage: null,
    failureCode: null,
    failureName: null,
    targetAccountId: null,
    slicesCompleted: 0,
    databasesLoaded: 0,
    dumpBytes: 0,
    hasIssuedSecrets: false,
    syncedAt: null,
    cutOverAt: null,
    ...fields,
    stateName: accountStateName(fields.state ?? 'Pending'),
  }
}

/** A dry run with one conflict. */
export const CONFLICT_REPORT: MigrationJobReport = {
  jobId: JOB_ID,
  canStart: false,
  findings: [
    {
      sourceAccountName: 'alice',
      moduleId: 'accounts',
      severity: 'Conflict',
      code: 'MigrationNameTaken',
      message: 'That name is already used on this server.',
      subject: 'alice',
    },
  ],
}

/** What the stub holds and records. */
export interface MigrationJobsStubState {
  /** The job. */
  job: MigrationJob
  /** The job's accounts. */
  accounts: MigrationJobAccount[]
  /** The dry run the panel answers. */
  report: MigrationJobReport
  /** Every request the page made, in order. */
  requests: { method: string; path: string; body: unknown }[]
}

/**
 * Answers a route with JSON.
 * @param route The route.
 * @param body The body.
 * @param status The status.
 * @returns Resolves once fulfilled.
 */
const json = (route: Route, body: unknown, status = 200): Promise<void> => {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Signs an administrator in, installs the shell stubs and the migration-job endpoints, and opens the landing page.
 * @param page The Playwright page under test.
 * @param seed What differs from the draft job and its two accounts.
 * @returns The stub's live state.
 */
export const openMigrationJobs = async (page: Page, seed: Partial<MigrationJobsStubState> = {}): Promise<MigrationJobsStubState> => {
  const state: MigrationJobsStubState = {
    job: structuredClone(DRAFT_JOB),
    accounts: [jobAccount('alice'), jobAccount('bob')],
    report: CONFLICT_REPORT,
    requests: [],
    ...structuredClone(seed),
  }

  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubModules(page, MIGRATION_MODULES)

  await page.route(/\/api\/v1\/migration-jobs/, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, path: url.pathname, body })
    const tail = url.pathname.replace('/api/v1/migration-jobs', '')

    if (method === 'GET' && tail === '') {
      return json(route, { items: [state.job], total: 1, page: 1, pageSize: 25 })
    }
    if (method === 'GET' && tail === `/${JOB_ID}`) {
      return json(route, state.job)
    }
    if (method === 'GET' && tail === `/${JOB_ID}/accounts`) {
      return json(route, { items: state.accounts, total: state.accounts.length, page: 1, pageSize: 25 })
    }
    if (method === 'POST' && tail === `/${JOB_ID}/dry-run`) {
      return json(route, state.report)
    }
    if (method === 'POST' && tail === `/${JOB_ID}/control`) {
      const action = (body as { action: string }).action
      const next = action === 'start' ? 'Running' : action === 'pause' ? 'Paused' : state.job.state
      state.job = { ...state.job, state: next, stateName: jobStateName(next) }
      return json(route, state.job)
    }
    return route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Not found', detail: 'There is no such resource.', status: 404 }) })
  })

  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()

  return state
}

/**
 * Reaches the migrations list by the sidebar, the way a person does.
 * @param page The Playwright page under test.
 * @returns Resolves once the list's heading is drawn.
 */
export const openMigrationJobsList = async (page: Page): Promise<void> => {
  await openSection(page, 'Moving accounts')
  await mainNavigation(page).getByRole('link', { name: 'Server migrations', exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Server migrations' })).toBeVisible()
}
