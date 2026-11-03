import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { AuditActionChoice, AuditEvent, AuditOutcomeChoice } from '../../src/types/audit'

/** The paged journal endpoint the audit page reads. */
export const AUDIT_PATTERN = /\/api\/v1\/audit(\?.*)?$/

/** The actions endpoint the action filter reads. */
const AUDIT_ACTIONS_PATTERN = /\/api\/v1\/audit\/actions(\?.*)?$/

/** The outcomes endpoint the outcome filter reads. */
export const AUDIT_OUTCOMES_PATTERN = /\/api\/v1\/audit\/outcomes(\?.*)?$/

/** The outcomes the panel lists, in the order it lists them, named as it names them in English. */
export const AUDIT_OUTCOME_CHOICES: AuditOutcomeChoice[] = [
  { outcome: 'succeeded', outcomeName: 'Succeeded' },
  { outcome: 'failed', outcomeName: 'Failed' },
]

/** How the journal endpoint searches (actor, impersonator, subject, address), sorts and filters, as the panel does. */
const AUDIT_RULES: PagedRules<AuditEvent> = {
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
  },
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
 * `outcome`, `from` and `to`, and `GET /api/v1/audit/actions` with the distinct actions the entries
 * hold, named as the entries name them, and `GET /api/v1/audit/outcomes` with the closed set of
 * outcomes the panel lists.
 * @param page The Playwright page whose network the routes are installed on.
 * @param events The journal, newest first — the panel's default order.
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubAudit = async (page: Page, events: AuditEvent[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []
  await page.route(AUDIT_PATTERN, async (route: Route) => {
    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(events, route.request().url(), AUDIT_RULES)),
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
  return () => {
    return listRequests
  }
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
