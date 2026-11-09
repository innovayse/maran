import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type {
  AuditActionChoice,
  AuditChainStatus,
  AuditChainVerification,
  AuditEvent,
  AuditOutcomeChoice,
  AuditRetention,
} from '../../src/types/audit'

/** The paged journal endpoint the audit page reads. */
export const AUDIT_PATTERN = /\/api\/v1\/audit(\?.*)?$/

/** The actions endpoint the action filter reads. */
const AUDIT_ACTIONS_PATTERN = /\/api\/v1\/audit\/actions(\?.*)?$/

/** The reseller's own part of the journal (issue #370). */
export const RESELLER_AUDIT_PATTERN = /\/api\/v1\/audit\/reseller(\?.*)?$/

/** The actions endpoint the reseller's action filter reads. */
const RESELLER_AUDIT_ACTIONS_PATTERN = /\/api\/v1\/audit\/reseller\/actions(\?.*)?$/

/** The outcomes endpoint the reseller's outcome filter reads. */
const RESELLER_AUDIT_OUTCOMES_PATTERN = /\/api\/v1\/audit\/reseller\/outcomes(\?.*)?$/

/** The outcomes endpoint the outcome filter reads. */
export const AUDIT_OUTCOMES_PATTERN = /\/api\/v1\/audit\/outcomes(\?.*)?$/

/** The outcomes the panel lists, in the order it lists them, named as it names them in English. */
export const AUDIT_OUTCOME_CHOICES: AuditOutcomeChoice[] = [
  { outcome: 'succeeded', outcomeName: 'Succeeded' },
  { outcome: 'failed', outcomeName: 'Failed' },
]

/** The export endpoint the audit page's export menu reads. */
export const AUDIT_EXPORT_PATTERN = /\/api\/v1\/audit\/export(\?.*)?$/

/** The retention endpoint the retention card reads and saves. */
export const AUDIT_RETENTION_PATTERN = /\/api\/v1\/audit\/retention(\?.*)?$/

/** The chain status endpoint the integrity card reads. */
export const AUDIT_CHAIN_PATTERN = /\/api\/v1\/audit\/chain(\?.*)?$/

/** The chain verification endpoint the integrity card's button asks. */
export const AUDIT_VERIFICATION_PATTERN = /\/api\/v1\/audit\/chain\/verification(\?.*)?$/

/** The retention the panel reports until a spec says otherwise: keep forever, bounds as the panel sets them. */
export const DEFAULT_RETENTION: AuditRetention = {
  retentionDays: null,
  minimumDays: 30,
  maximumDays: 3650,
  lastRunAt: null,
}

/** The chain the panel reports until a spec says otherwise: mostly sealed, a few entries waiting. */
export const DEFAULT_CHAIN: AuditChainStatus = {
  sealedRows: 1200,
  unsealedRows: 34,
  lastChainIndex: 1199,
  lastSealedAt: '2026-09-10T08:00:00+00:00',
  purgedThroughIndex: 0,
}

/** The verdict of a chain nothing has touched. */
export const INTACT_VERDICT: AuditChainVerification = {
  intact: true,
  rowsChecked: 1200,
  firstBrokenChainIndex: null,
  break: 'None',
  verifiedAt: '2026-09-10T09:00:00+00:00',
}

/**
 * How the journal endpoint searches (actor, impersonator, subject, address), sorts and filters, as
 * the panel does. The `actor` filter is an exact, case-insensitive match on the login name; the
 * `account` filter keeps the entries the panel attributed to that account, and the DTO carries no
 * account, so the attribution is the spec's map.
 * @param accountOf The account each entry (by id) was attributed to; an entry absent from it has none.
 * @returns The rules.
 */
const auditRules = (accountOf: Readonly<Record<string, string>>): PagedRules<AuditEvent> => {
  // Typed as a named constant so each rule below is contextually typed, not an untyped arrow.
  const rules: PagedRules<AuditEvent> = {
    matches: (event, term) => {
      return [event.actorUsername, event.impersonatorUsername, event.subject, event.ipAddress].some((text) => {
        return text !== null && text.toLowerCase().includes(term)
      })
    },
    sorts: {
      occurredAt: (event) => {
        return event.occurredAt
      },
      actor: (event) => {
        return event.actorUsername
      },
      action: (event) => {
        return event.action
      },
    },
    filters: {
      action: (event, value) => {
        return event.action === value
      },
      outcome: (event, value) => {
        return (value === 'succeeded') === event.succeeded
      },
      from: (event, value) => {
        return Date.parse(event.occurredAt) >= Date.parse(value)
      },
      to: (event, value) => {
        return Date.parse(event.occurredAt) <= Date.parse(value)
      },
      actor: (event, value) => {
        return event.actorUsername.toLowerCase() === value.toLowerCase()
      },
      account: (event, value) => {
        return accountOf[event.id] === value
      },
    },
  }
  return rules
}

/**
 * Builds a journal entry written while an administrator was viewing the panel as the actor.
 * @param base The entry as it would read had the actor acted for themselves.
 * @param impersonatorUsername The administrator who was viewing as the actor.
 * @returns The same entry, carrying the operator's name.
 */
export const viewedAs = (base: AuditEvent, impersonatorUsername: string): AuditEvent => {
  return { ...base, impersonatorUsername }
}

/**
 * Fulfils `GET /api/v1/audit` as a page honouring `page`, `pageSize`, `search`, `sort`, `action`,
 * `outcome`, `from`, `to`, `actor` and `account`, and `GET /api/v1/audit/actions` with the distinct actions the entries
 * hold, named as the entries name them, and `GET /api/v1/audit/outcomes` with the closed set of
 * outcomes the panel lists.
 * It also installs the retention and chain reads with their defaults, so a spec that only looks at
 * the journal still gets a page whose cards have something to read; a spec about the cards
 * installs its own after, which answers first.
 * @param page The Playwright page whose network the routes are installed on.
 * @param events The journal, newest first — the panel's default order.
 * @param accountOf The account each entry (by id) was attributed to, for the `account` filter.
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubAudit = async (
  page: Page,
  events: AuditEvent[],
  accountOf: Readonly<Record<string, string>> = {},
): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []
  await page.route(AUDIT_PATTERN, async (route: Route) => {
    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(events, route.request().url(), auditRules(accountOf))),
    })
  })
  const actions = new Map<string, AuditActionChoice>()
  for (const event of events) {
    actions.set(event.action, { action: event.action, actionName: event.actionName })
  }
  await page.route(AUDIT_ACTIONS_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([...actions.values()]) })
  })
  await page.route(AUDIT_OUTCOMES_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(AUDIT_OUTCOME_CHOICES) })
  })
  await stubAuditRetention(page)
  await stubAuditChain(page)
  return () => {
    return listRequests
  }
}

/**
 * Fulfils the reseller's part of the journal the way the panel does: `GET /api/v1/audit/reseller` as a
 * page honouring the same search, sort and filters as the administrator's, and its `actions` and
 * `outcomes` choices. The entries handed in ARE the reseller's part; deciding which entries a reseller
 * may read is the panel's job and is held by the backend's tests, so nothing here narrows them.
 * @param page The Playwright page whose network the routes are installed on.
 * @param events The reseller's part of the journal, newest first.
 * @param accountOf The account each entry (by id) was attributed to, for the `account` filter.
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubResellerAudit = async (
  page: Page,
  events: AuditEvent[],
  accountOf: Readonly<Record<string, string>> = {},
): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []
  await page.route(RESELLER_AUDIT_PATTERN, async (route: Route) => {
    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(events, route.request().url(), auditRules(accountOf))),
    })
  })
  const actions = new Map<string, AuditActionChoice>()
  for (const event of events) {
    actions.set(event.action, { action: event.action, actionName: event.actionName })
  }
  await page.route(RESELLER_AUDIT_ACTIONS_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([...actions.values()]) })
  })
  await page.route(RESELLER_AUDIT_OUTCOMES_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(AUDIT_OUTCOME_CHOICES) })
  })
  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/audit/retention` with the setting, and `PUT` with the panel's rule: a value
 * outside the bounds is a 400 carrying the panel's sentence, anything else is stored and answered
 * 204, and the next read reports it.
 * @param page The Playwright page whose network the routes are installed on.
 * @param initial The setting the panel holds before any save.
 * @returns A function reporting every day count a save sent so far (`null` for keep forever), oldest first.
 */
export const stubAuditRetention = async (
  page: Page,
  initial: AuditRetention = DEFAULT_RETENTION,
): Promise<() => readonly (number | null)[]> => {
  let current = initial
  const saved: (number | null)[] = []
  await page.route(AUDIT_RETENTION_PATTERN, async (route: Route) => {
    if (route.request().method() === 'PUT') {
      const sent = route.request().postDataJSON() as { retentionDays: number | null }
      saved.push(sent.retentionDays)
      if (
        sent.retentionDays !== null &&
        (sent.retentionDays < current.minimumDays || sent.retentionDays > current.maximumDays)
      ) {
        await route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            code: 'AuditRetentionOutOfRange',
            title: 'Invalid',
            detail: `Keep entries for between ${current.minimumDays} and ${current.maximumDays} days.`,
          }),
        })
        return
      }
      current = { ...current, retentionDays: sent.retentionDays }
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })
  return () => {
    return saved
  }
}

/**
 * Fulfils `GET /api/v1/audit/chain` with the chain's state and `POST /api/v1/audit/chain/verification`
 * with a verdict.
 * @param page The Playwright page whose network the routes are installed on.
 * @param status The chain the panel reports.
 * @param verdict What a verification answers.
 * @returns A function reporting how many verifications were asked for so far.
 */
export const stubAuditChain = async (
  page: Page,
  status: AuditChainStatus = DEFAULT_CHAIN,
  verdict: AuditChainVerification = INTACT_VERDICT,
): Promise<() => number> => {
  let verifications = 0
  await page.route(AUDIT_CHAIN_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(status) })
  })
  await page.route(AUDIT_VERIFICATION_PATTERN, async (route: Route) => {
    verifications += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(verdict) })
  })
  return () => {
    return verifications
  }
}

/**
 * Fulfils `GET /api/v1/audit/export` with a file the way the panel streams one: the body, its type
 * and a `Content-Disposition` naming it.
 * @param page The Playwright page whose network the routes are installed on.
 * @param file The file the panel sends: its body, content type and proposed name.
 * @returns A function reporting every export request so far, oldest first.
 */
export const stubAuditExport = async (
  page: Page,
  file: { body: string; contentType: string; fileName: string },
): Promise<() => readonly URL[]> => {
  const requests: URL[] = []
  await page.route(AUDIT_EXPORT_PATTERN, async (route: Route) => {
    requests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: file.contentType,
      headers: { 'Content-Disposition': `attachment; filename="${file.fileName}"` },
      body: file.body,
    })
  })
  return () => {
    return requests
  }
}

/**
 * Fulfils `GET /api/v1/audit/export` with an RFC 7807 refusal, so a spec can assert the page shows
 * the panel's sentence verbatim.
 * @param page The Playwright page whose network the routes are installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubAuditExportRefused = async (page: Page, detail: string): Promise<void> => {
  await page.route(AUDIT_EXPORT_PATTERN, async (route: Route) => {
    await route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'AuditExportTooLarge', title: 'Invalid', detail }),
    })
  })
}

/**
 * Fulfils `GET /api/v1/audit` with an RFC 7807 problem, so a spec can assert the page renders the
 * panel's own message verbatim. The actions answer empty.
 * @param page The Playwright page whose network the routes are installed on.
 * @param status The HTTP status of the refusal.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the routes are installed.
 */
export const stubAuditProblem = async (page: Page, status: number, detail: string): Promise<void> => {
  await stubAudit(page, [])
  await page.route(AUDIT_PATTERN, async (route: Route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'Refused', title: detail, detail }),
    })
  })
}
