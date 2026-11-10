import { expect, type Page, type Route } from '@playwright/test'
import { MIGRATION_MODULES } from './migration'
import { grantStateName, importStateName } from './migration-state-names'
import { mainNavigation, openSection } from './open-full-shell'
import { pagedAnswer, type PagedRules } from './paged-answer'
import { stubAccounts } from './stub-accounts-route'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubLandingReads } from './stub-landing-reads'
import { stubModules } from './stub-modules-route'
import { stubPlans } from './stub-plans-route'
import { stubShellReads } from './stub-shell-reads'
import type { Account } from '../../src/types/account'
import type {
  AccountExport,
  AccountImport,
  AccountImportReport,
  ExportGrant,
  ExportGrantSummary,
  IssuedSecret,
  MigrationFinding,
} from '../../src/types/accountMove'
import type { ResellerChoice } from '../../src/types/reseller'

/** The whole export API, under which one handler dispatches by path. */
const EXPORTS_API = /\/api\/v1\/account-exports/

/** The whole import API, under which one handler dispatches by path. */
const IMPORTS_API = /\/api\/v1\/account-imports/

/** The reseller picker's read, with or without its query. */
const RESELLER_CHOICES_API = /\/api\/v1\/resellers\/choices(\?.*)?$/

/** The name the sidebar section holding the migration pages is announced by. */
export const MIGRATION_SECTION = 'Moving accounts'

/** The account the export specs export. */
export const ALICE_ACCOUNT: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/** The fingerprint of the key the stubbed sending server signs with. */
export const SOURCE_FINGERPRINT = 'SHA256:Zm9vYmFyYmF6cXV4Y29yZ2VncmF1bHRnYXJwbHk'

/** The identity of the export the stub makes. */
export const EXPORT_ID = 'e0e0e0e0-0000-4000-8000-000000000001'

/** The identity of the grant the stub issues. */
export const GRANT_ID = 'a1a1a1a1-0000-4000-8000-000000000001'

/** The token of the grant the stub issues; a spec asserts that it vanishes with its dialog. */
export const GRANT_TOKEN = 'grant-token-for-specs-0123456789abcdef0123456789abcdef'

/** The identity of the import the stub stages. */
export const IMPORT_ID = 'b2b2b2b2-0000-4000-8000-000000000001'

/** The identity of the account a completed import created. */
export const TARGET_ACCOUNT_ID = 'c3c3c3c3-0000-4000-8000-000000000001'

/** The identity of the reseller the picker offers. */
export const RESELLER_ID = 'd4d4d4d4-0000-4000-8000-000000000001'

/** The plan the picker offers; the same one `stubPlans` serves by default. */
export const PLAN_ID = '22222222-2222-2222-2222-222222222222'

/** The code of the finding that says cron entries were imported disabled. */
export const CRON_DISABLED_CODE = 'MigrationCronImportedDisabled'

/** A finding that blocks the import: the account name is taken here. */
export const NAME_TAKEN_FINDING: MigrationFinding = {
  moduleId: 'accounts',
  severity: 'Conflict',
  code: 'MigrationNameTaken',
  message: 'An account called alice already exists on this server.',
  subject: 'alice',
}

/** A finding that only warns. */
export const PHP_WARNING_FINDING: MigrationFinding = {
  moduleId: 'sites',
  severity: 'Warning',
  code: 'MigrationPhpVersionMissing',
  message: 'PHP 7.4 is not installed on this server; the site will use the default version.',
  subject: 'alice.example.com',
}

/** The notice that cron entries come in switched off, with the sentence the panel would have localized. */
export const CRON_FINDING: MigrationFinding = {
  moduleId: 'cron',
  severity: 'Info',
  code: CRON_DISABLED_CODE,
  message: 'Cron entries are imported switched off. Enable the ones that ran on the old server when you cut over.',
  subject: 'alice',
}

/** The credentials a completed import hands over once. */
export const GENERATED_SECRETS: IssuedSecret[] = [
  { subject: 'Database user alice_main', secret: 'Db-Secret-For-Specs-91827364' },
  { subject: 'SFTP login alice', secret: 'Sftp-Secret-For-Specs-55667788' },
]

/** The sentence the panel resolves from {@link ROLLED_BACK_CODE} in the caller's language. */
export const ROLLED_BACK_NAME = 'The import stopped part-way and the account it had created was removed again.'

/** The code the stub's failed job stores. */
export const ROLLED_BACK_CODE = 'MigrationRolledBack'

/** One refused request the stub was told to answer with a problem body. */
export interface AccountMoveRefusal {
  /** The RFC 7807 `detail`, already localized the way the panel's own text is. */
  detail: string
  /** The status to answer with; 409 when left out. */
  status?: number
}

/** What a spec seeds the stub with, and reads back after the page acted. */
export interface AccountMoveStubState {
  /** The accounts the export picker offers. */
  accounts: Account[]
  /** The exports the stub holds. */
  exports: AccountExport[]
  /** The imports the stub holds. */
  imports: AccountImport[]
  /** The pull grants the stub holds; a grant issued through the stub is added, a revoked one changes state. */
  grants: ExportGrantSummary[]
  /** The findings the dry run reports. */
  findings: MigrationFinding[]
  /** The credentials the import generated, claimable once. */
  secrets: IssuedSecret[]
  /** Whether the secrets were already claimed, which the panel answers with a refusal. */
  secretsClaimed: boolean
  /** How many reads of a running import answer `Running` before the job ends. */
  runningReads: number
  /** Whether the started job fails and is rolled back instead of completing. */
  failImport: boolean
  /** The grants revoked so far. */
  revokedGrants: string[]
  /** The size and fingerprint of every upload the stub accepted. */
  uploads: { bytes: number; fingerprint: string }[]
  /** Requests to refuse, by `"<METHOD> <shape>"`, with `:id` for each identity. */
  refusals: Record<string, AccountMoveRefusal>
  /** Every request the stub saw, oldest first. */
  requests: { method: string; url: URL; body: unknown }[]
}

/** The export the stub makes for an account, shaped like the panel's `AccountExportDto`. */
export const exportOf = (account: Account): AccountExport => {
  return {
    id: EXPORT_ID,
    accountName: account.name,
    sizeBytes: 48 * 1024 * 1024,
    sha256: 'a'.repeat(64),
    databaseCount: 2,
    sectionModules: ['accounts', 'sites', 'databases', 'cron'],
    keyFingerprint: SOURCE_FINGERPRINT,
    createdAt: '2026-11-09T10:00:00Z',
    expiresAt: '2026-11-16T10:00:00Z',
  }
}

/** A freshly staged import of {@link ALICE_ACCOUNT}'s package, shaped like the panel's `AccountImportDto`. */
export const STAGED_IMPORT: AccountImport = {
  id: IMPORT_ID,
  state: 'Staged',
  stateName: importStateName('Staged'),
  accountName: 'alice',
  primaryDomain: 'alice.example.com',
  sourceHostName: 'old-panel.example.net',
  keyFingerprint: SOURCE_FINGERPRINT,
  sizeBytes: 48 * 1024 * 1024,
  homeBytes: 120 * 1024 * 1024,
  databaseCount: 2,
  sectionModules: ['accounts', 'sites', 'databases', 'cron'],
  targetAccountId: null,
  stage: null,
  failureCode: null,
  failureName: null,
  hasIssuedSecrets: false,
  createdAt: '2026-11-09T10:00:00Z',
  finishedAt: null,
}

/**
 * An import in any state, shaped like the panel's `AccountImportDto`, for a spec that seeds a list.
 * @param overrides What differs from {@link STAGED_IMPORT}; the identity should be set.
 * @returns The import.
 */
export const importOf = (overrides: Partial<AccountImport>): AccountImport => {
  return { ...STAGED_IMPORT, ...overrides, stateName: importStateName(overrides.state ?? 'Staged') }
}

/**
 * A pull grant, shaped like the panel's `ExportGrantSummaryDto`, for a spec that seeds the grants list.
 * @param overrides What differs from a live grant on alice's export; the identity should be set.
 * @returns The grant.
 */
export const grantOf = (overrides: Partial<ExportGrantSummary>): ExportGrantSummary => {
  return {
    id: GRANT_ID,
    exportId: EXPORT_ID,
    accountName: 'alice',
    state: 'Live',
    allowedFrom: null,
    manifestReads: 0,
    createdAt: '2026-11-09T10:00:00Z',
    expiresAt: '2026-11-09T10:30:00Z',
    artifactConsumedAt: null,
    revokedAt: null,
    ...overrides,
    stateName: grantStateName(overrides.state ?? 'Live'),
  }
}

/** How the exports list's stub searches and sorts: by the account's name, as the panel does. */
const EXPORT_RULES: PagedRules<AccountExport> = {
  matches: (row, term) => {
    return row.accountName.toLowerCase().includes(term)
  },
  sorts: {
    accountName: (row) => {
      return row.accountName
    },
    createdAt: (row) => {
      return row.createdAt
    },
    expiresAt: (row) => {
      return row.expiresAt
    },
  },
  filters: {},
}

/** How the imports list's stub searches, sorts and filters: the account or sending server's name, and the state. */
const IMPORT_RULES: PagedRules<AccountImport> = {
  matches: (row, term) => {
    return row.accountName.toLowerCase().includes(term) || row.sourceHostName.toLowerCase().includes(term)
  },
  sorts: {
    accountName: (row) => {
      return row.accountName
    },
    sourceHostName: (row) => {
      return row.sourceHostName
    },
    state: (row) => {
      return row.state
    },
    createdAt: (row) => {
      return row.createdAt
    },
  },
  filters: {
    state: (row, value) => {
      return row.state === value
    },
  },
}

/** How the grants list's stub searches, sorts and filters: the account's name, and the state. */
const GRANT_RULES: PagedRules<ExportGrantSummary> = {
  matches: (row, term) => {
    return row.accountName.toLowerCase().includes(term)
  },
  sorts: {
    accountName: (row) => {
      return row.accountName
    },
    createdAt: (row) => {
      return row.createdAt
    },
    expiresAt: (row) => {
      return row.expiresAt
    },
  },
  filters: {
    state: (row, value) => {
      return row.state === value
    },
  },
}

/** How the reseller picker's stub searches: by name. */
const RESELLER_RULES: PagedRules<ResellerChoice> = {
  matches: (reseller, term) => {
    return reseller.name.toLowerCase().includes(term)
  },
  sorts: {},
  filters: {},
}

/**
 * Answers a request with a JSON body.
 * @param route The intercepted request.
 * @param body The body.
 * @param status The status; 200 when left out.
 * @returns Resolves once the response is sent.
 */
const json = async (route: Route, body: unknown, status = 200): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Answers a request with an RFC 7807 problem body.
 * @param route The intercepted request.
 * @param detail The localized explanation.
 * @param status The status; 409 when left out.
 * @returns Resolves once the response is sent.
 */
const problem = async (route: Route, detail: string, status = 409): Promise<void> => {
  await route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'AccountMoveStubRefusal', title: 'Refused', detail, status }),
  })
}

/**
 * Turns a path into the shape a refusal is keyed by: the identities become `:id`.
 * @param pathname The request path.
 * @returns The shape, such as `account-exports/:id/grants`.
 */
const shapeOf = (pathname: string): string => {
  return pathname
    .replace('/api/v1/', '')
    .split('/')
    .map((part) => {
      return /^[0-9a-f]{8}-/.test(part) ? ':id' : part
    })
    .join('/')
}

/**
 * Reads the import as the stub currently holds it, advancing a running job by one read.
 * @param state The live state.
 * @param current The import held.
 * @returns The import the panel would answer with.
 */
const advance = (state: AccountMoveStubState, current: AccountImport): AccountImport => {
  if (current.state !== 'Running') {
    return current
  }
  if (state.runningReads > 0) {
    state.runningReads -= 1
    return current
  }
  const finished: AccountImport = state.failImport
    ? { ...current, state: 'RolledBack', stateName: importStateName('RolledBack'), stage: null, failureCode: ROLLED_BACK_CODE, failureName: ROLLED_BACK_NAME, finishedAt: '2026-11-09T10:09:00Z' }
    : {
        ...current,
        state: 'Completed',
        stateName: importStateName('Completed'),
        stage: null,
        targetAccountId: TARGET_ACCOUNT_ID,
        hasIssuedSecrets: state.secrets.length > 0,
        finishedAt: '2026-11-09T10:09:00Z',
      }
  Object.assign(current, finished)
  return finished
}

/**
 * Installs the export and import endpoints of the panel over a mutable state, with the contract's
 * shapes. No answer ever carries a signing key, as the real endpoints never do; the token of a grant
 * and the generated secrets are answered once, the way the panel does.
 * @param page The Playwright page whose network the routes are installed on.
 * @param seed What the panel holds; anything left out starts empty or at its default.
 * @returns The live state.
 */
export const stubAccountMove = async (page: Page, seed: Partial<AccountMoveStubState> = {}): Promise<AccountMoveStubState> => {
  const state: AccountMoveStubState = {
    accounts: [ALICE_ACCOUNT],
    exports: [],
    imports: [],
    grants: [],
    findings: [],
    secrets: GENERATED_SECRETS,
    secretsClaimed: false,
    runningReads: 0,
    failImport: false,
    revokedGrants: [],
    uploads: [],
    refusals: {},
    requests: [],
    // A copy: a test changes the live state, and the seed's objects are shared fixtures every
    // other test in the worker reads.
    ...structuredClone(seed),
  }

  await page.route(EXPORTS_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    const shape = shapeOf(url.pathname)
    const refusal = state.refusals[`${method} ${shape}`]
    if (refusal !== undefined) {
      await problem(route, refusal.detail, refusal.status)
      return
    }

    const parts = url.pathname.replace('/api/v1/', '').split('/')
    const found = state.exports.find((candidate) => {
      return candidate.id === parts[1]
    })
    const notFound = (): Promise<void> => {
      return problem(route, 'There is no such export.', 404)
    }

    switch (`${method} ${shape}`) {
      case 'POST account-exports': {
        const account = state.accounts.find((candidate) => {
          return candidate.id === (body as { accountId: string }).accountId
        })
        if (account === undefined) {
          return notFound()
        }
        const made = exportOf(account)
        state.exports = [made]
        return json(route, made)
      }
      case 'GET account-exports':
        return json(route, pagedAnswer(state.exports, request.url(), EXPORT_RULES))
      case 'GET account-exports/grants':
        return json(route, pagedAnswer(state.grants, request.url(), GRANT_RULES))
      case 'GET account-exports/:id':
        return found === undefined ? notFound() : json(route, found)
      case 'GET account-exports/:id/package':
        if (found === undefined) {
          return notFound()
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/octet-stream',
          headers: { 'Content-Disposition': `attachment; filename="${found.accountName}.maranpkg"` },
          body: 'MARANPKG1 package bytes for specs',
        })
      case 'POST account-exports/:id/grants': {
        if (found === undefined) {
          return notFound()
        }
        const asked = body as { allowedFrom: string | null; lifetimeMinutes: number | null }
        const grant: ExportGrant = {
          id: GRANT_ID,
          exportId: found.id,
          token: GRANT_TOKEN,
          allowedFrom: asked.allowedFrom,
          expiresAt: '2026-11-09T10:30:00Z',
        }
        state.grants = [
          grantOf({ id: grant.id, exportId: found.id, accountName: found.accountName, allowedFrom: grant.allowedFrom, expiresAt: grant.expiresAt }),
          ...state.grants,
        ]
        return json(route, grant)
      }
      case 'DELETE account-exports/grants/:id': {
        const revoked = state.grants.find((candidate) => {
          return candidate.id === parts[2]
        })
        if (revoked !== undefined) {
          Object.assign(revoked, { state: 'Revoked', stateName: grantStateName('Revoked'), revokedAt: '2026-11-09T10:10:00Z' })
        }
        state.revokedGrants.push(parts[2] ?? '')
        return route.fulfill({ status: 204 })
      }
      default:
        return notFound()
    }
  })

  await page.route(IMPORTS_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const isUpload = method === 'POST' && shapeOf(url.pathname) === 'account-imports'
    const body: unknown = isUpload || request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    const shape = shapeOf(url.pathname)
    const refusal = state.refusals[`${method} ${shape}`]
    if (refusal !== undefined) {
      await problem(route, refusal.detail, refusal.status)
      return
    }

    const parts = url.pathname.replace('/api/v1/', '').split('/')
    const found = state.imports.find((candidate) => {
      return candidate.id === parts[1]
    })
    const notFound = (): Promise<void> => {
      return problem(route, 'There is no such import.', 404)
    }

    switch (`${method} ${shape}`) {
      case 'POST account-imports': {
        const fingerprint = url.searchParams.get('fingerprint') ?? ''
        state.uploads.push({ bytes: (request.postData() ?? '').length, fingerprint })
        if (fingerprint !== SOURCE_FINGERPRINT) {
          return problem(route, 'The package was not signed by the key with that fingerprint.', 400)
        }
        const staged: AccountImport = { ...STAGED_IMPORT }
        state.imports = [staged]
        return json(route, staged)
      }
      case 'GET account-imports':
        return json(route, pagedAnswer(state.imports, request.url(), IMPORT_RULES))
      case 'GET account-imports/:id':
        return found === undefined ? notFound() : json(route, advance(state, found))
      case 'GET account-imports/:id/report': {
        if (found === undefined) {
          return notFound()
        }
        const report: AccountImportReport = {
          importId: found.id,
          canStart: !state.findings.some((finding) => {
            return finding.severity === 'Conflict'
          }),
          findings: state.findings,
        }
        return json(route, report)
      }
      case 'POST account-imports/:id/start': {
        if (found === undefined) {
          return notFound()
        }
        const asked = body as { confirmAccountName: string; confirmKeyFingerprint: string }
        if (asked.confirmAccountName !== found.accountName || asked.confirmKeyFingerprint !== found.keyFingerprint) {
          return problem(route, 'The confirmation does not match the account and the key.', 409)
        }
        Object.assign(found, { state: 'Running', stateName: importStateName('Running'), stage: 'databases' })
        return json(route, found)
      }
      case 'DELETE account-imports/:id':
        if (found === undefined) {
          return notFound()
        }
        Object.assign(found, { state: 'Discarded', stateName: importStateName('Discarded'), finishedAt: '2026-11-09T10:05:00Z' })
        return json(route, found)
      case 'POST account-imports/:id/secrets':
        if (found === undefined) {
          return notFound()
        }
        if (state.secretsClaimed) {
          return problem(route, 'The credentials were already shown.', 409)
        }
        state.secretsClaimed = true
        Object.assign(found, { hasIssuedSecrets: false })
        return json(route, state.secrets)
      default:
        return notFound()
    }
  })

  return state
}

/**
 * Signs an administrator in on a desktop, installs the stubs the account-move screens read (the
 * accounts, plans and resellers the pickers offer, the export and import endpoints) and opens the
 * panel's landing page, from which a spec reaches the screens by clicking.
 * @param page The Playwright page under test.
 * @param seed What the panel holds; see {@link stubAccountMove}.
 * @returns The stub's live state.
 */
export const openAccountMoveScreen = async (page: Page, seed: Partial<AccountMoveStubState> = {}): Promise<AccountMoveStubState> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubLandingReads(page)
  await stubHealthy(page)
  await stubModules(page, MIGRATION_MODULES)
  const state = await stubAccountMove(page, seed)
  await stubAccounts(page, structuredClone(state.accounts))
  await stubPlans(page)
  await page.route(RESELLER_CHOICES_API, async (route) => {
    const resellers: ResellerChoice[] = [{ id: RESELLER_ID, name: 'Acme Hosting' }]
    await json(route, pagedAnswer(resellers, route.request().url(), RESELLER_RULES))
  })
  await page.goto('/')
  await expect(page.locator('nav [data-nav-section]').first()).toBeVisible()
  return state
}

/**
 * Reaches a page of the migration section the way a person does: opens the section's submenu, then
 * follows the link.
 * @param page The Playwright page under test.
 * @param link The sidebar link's name.
 * @param heading The page's `h1`.
 * @returns Resolves once the page's heading is drawn.
 */
export const openMigrationPage = async (page: Page, link: string, heading: string): Promise<void> => {
  await openSection(page, MIGRATION_SECTION)
  await mainNavigation(page).getByRole('link', { name: link, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
}

/**
 * Reaches the export screen by the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen's heading is drawn.
 */
export const openAccountExport = async (page: Page): Promise<void> => {
  await openMigrationPage(page, 'Export an account', 'Export an account')
}

/**
 * Reaches the import screen by the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the screen's heading is drawn.
 */
export const openAccountImport = async (page: Page): Promise<void> => {
  await openMigrationPage(page, 'Import an account', 'Import an account')
}

/**
 * Reaches the exports list by the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the list's heading is drawn.
 */
export const openAccountExports = async (page: Page): Promise<void> => {
  await openMigrationPage(page, 'Exports', 'Account exports')
}

/**
 * Reaches the imports list by the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the list's heading is drawn.
 */
export const openAccountImports = async (page: Page): Promise<void> => {
  await openMigrationPage(page, 'Imports', 'Account imports')
}

/**
 * Reaches the pull grants list by the sidebar.
 * @param page The Playwright page under test.
 * @returns Resolves once the list's heading is drawn.
 */
export const openExportGrants = async (page: Page): Promise<void> => {
  await openMigrationPage(page, 'Pull grants', 'Pull grants')
}
