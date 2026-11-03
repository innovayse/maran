import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import { PLAN_FEATURE_CHOICES } from './stub-plans-route'
import { OWNER_LANGUAGES } from './stub-accounts-route'
import type {
  ChangeResellerEnvelopeRequest,
  CreateResellerRequest,
  Reseller,
  ResellerChoice,
  ResellerDetail,
  ResellerEnvelope,
} from '../../src/types/reseller'

/** The resellers collection: the paged list (`GET`) and the create form (`POST`), with or without a query. */
const RESELLERS_PATTERN = /\/api\/v1\/resellers(\?.*)?$/

/** The reseller choices the owner pickers read, with or without a query. */
const CHOICES_PATTERN = /\/api\/v1\/resellers\/choices(\?.*)?$/

/** One reseller by its identity: the detail read and the delete. */
const RESELLER_PATTERN = /\/api\/v1\/resellers\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/

/** The envelope write for one reseller. */
const ENVELOPE_PATTERN = /\/api\/v1\/resellers\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/envelope$/

/** The suspend action for one reseller. */
const SUSPEND_PATTERN = /\/api\/v1\/resellers\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/suspend$/

/** The reactivate action for one reseller. */
const REACTIVATE_PATTERN = /\/api\/v1\/resellers\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/reactivate$/

/** The features a plan can sell: the envelope form offers the same list. */
const FEATURES_PATTERN = /\/api\/v1\/plans\/features(\?.*)?$/

/** The owner languages the reseller form's select reads. */
const OWNER_LANGUAGES_PATTERN = /\/api\/v1\/accounts\/owner-languages(\?.*)?$/

/** The identity the stub gives a reseller created through the form. */
export const CREATED_RESELLER_ID = '77777777-7777-7777-7777-777777777777'

/**
 * An envelope with distinct figures, so a spec reading one of them back can tell which field it is.
 * Every figure sits well inside what the form accepts.
 */
export const RESELLER_ENVELOPE: ResellerEnvelope = {
  maxAccounts: 20,
  diskPoolMb: 102400,
  maxPlans: 6,
  maxDiskQuotaMb: 20480,
  maxSites: 12,
  maxDatabases: 8,
  maxSftpUsers: 6,
  maxFtpUsers: 4,
  maxCronEntries: 9,
  maxPhpWorkersPerPool: 7,
  cpuQuotaPercent: { kind: 'limited', maximum: 200 },
  memoryMaxMb: { kind: 'limited', maximum: 2048 },
  maxProcesses: { kind: 'unlimited' },
  features: ['databases', 'cron', 'fileTransfer', 'backups'],
}

/** A reseller that is active and has sold something. */
export const ACME_HOSTING: ResellerDetail = {
  id: '00000000-0000-0000-0000-0000000000e1',
  name: 'acmehosting',
  contactEmail: 'owner@acmehosting.example.com',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-10-20T09:00:00+00:00',
  accountCount: 3,
  planCount: 2,
  diskAllocatedMb: 30720,
  language: 'en',
  envelope: RESELLER_ENVELOPE,
}

/** A second reseller, suspended, so the list has both states and a search has something to narrow. */
export const BRAVO_HOSTING: ResellerDetail = {
  ...ACME_HOSTING,
  id: '00000000-0000-0000-0000-0000000000e2',
  name: 'bravohosting',
  contactEmail: 'owner@bravohosting.example.com',
  status: 'suspended',
  statusName: 'Suspended',
  accountCount: 0,
  planCount: 0,
  diskAllocatedMb: 0,
}

/** What a resellers stub records of the requests it served, so a spec can assert what was sent. */
export interface ResellerRequests {
  /** The body of every `POST /api/v1/resellers`, in order. */
  created: CreateResellerRequest[]
  /** The path id and body of every `PUT /api/v1/resellers/{id}/envelope`, in order. */
  envelopes: { id: string; body: ChangeResellerEnvelopeRequest }[]
  /** The id of every suspend request, in order. */
  suspended: string[]
  /** The id of every reactivate request, in order. */
  reactivated: string[]
  /** The id of every `DELETE /api/v1/resellers/{id}`, in order. */
  deleted: string[]
}

/**
 * The server's refusals a spec can make the stub answer with, each as the RFC 7807 problem the real
 * panel sends. The text is the spec's, so a spec can prove it is rendered verbatim.
 */
export interface ResellerRefusals {
  /** Refuses every create, as the real panel does for a name another reseller has. */
  create?: string
  /** Refuses every suspend. */
  suspend?: string
  /** Refuses every reactivate. */
  reactivate?: string
  /** Refuses every delete, as the real panel does for a reseller that still owns accounts. */
  remove?: string
}

/** How the resellers endpoint searches, sorts and filters, as the panel does. */
const RESELLER_RULES: PagedRules<Reseller> = {
  matches: (reseller, term) => {
    return reseller.name.toLowerCase().includes(term) || reseller.contactEmail.toLowerCase().includes(term)
  },
  sorts: {
    name: (reseller) => {
      return reseller.name
    },
    status: (reseller) => {
      return reseller.status
    },
    createdAt: (reseller) => {
      return reseller.createdAt
    },
  },
  filters: {
    status: (reseller, value) => {
      return reseller.status === value
    },
  },
}

/** How the choices endpoint searches: by name only. */
const CHOICE_RULES: PagedRules<Reseller> = {
  matches: (reseller, term) => {
    return reseller.name.toLowerCase().includes(term)
  },
  sorts: {},
  filters: {},
}

/**
 * Answers with a refusal the way the panel does.
 * @param route The intercepted request.
 * @param code The problem's stable code.
 * @param detail The message to put in the problem body.
 * @returns Resolves once the response is sent.
 */
const refuse = async (route: Route, code: string, detail: string): Promise<void> => {
  await route.fulfill({
    status: 409,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code, title: 'Conflict', detail }),
  })
}

/**
 * Answers the not-found the panel sends for a reseller that is not there.
 * @param route The intercepted request.
 * @returns Resolves once the response is sent.
 */
const notFound = async (route: Route): Promise<void> => {
  await route.fulfill({
    status: 404,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'ResellerNotFound', title: 'Not found', detail: 'No such reseller.' }),
  })
}

/**
 * Fulfils the reseller-management endpoints from one in-memory catalogue: the paged list with
 * search, sort and the `status` filter, `POST` create, one reseller by id, the envelope write,
 * suspend, reactivate and delete, the choices page, and the two reads the reseller form needs (the
 * features a plan can sell and the owner languages). The catalogue changes as the page changes it,
 * the way the server's does, so a list the page returns to shows the result.
 * @param page The Playwright page whose network the routes are installed on.
 * @param resellers The resellers the panel starts with; mutated by successful writes.
 * @param refusals Writes to refuse with a problem body instead of performing them.
 * @returns What the stub received, filled in as the page makes requests.
 */
export const stubResellers = async (
  page: Page,
  resellers: ResellerDetail[] = [ACME_HOSTING, BRAVO_HOSTING].map((reseller) => {
    return { ...reseller }
  }),
  refusals: ResellerRefusals = {},
): Promise<ResellerRequests> => {
  const requests: ResellerRequests = { created: [], envelopes: [], suspended: [], reactivated: [], deleted: [] }

  /**
   * Finds the reseller a request path names.
   * @param route The intercepted request.
   * @param pattern The pattern whose first group is the reseller's id.
   * @returns The reseller, or `undefined` when the id names none.
   */
  const find = (route: Route, pattern: RegExp): ResellerDetail | undefined => {
    const id = pattern.exec(new URL(route.request().url()).pathname)?.[1] ?? ''
    return resellers.find((candidate) => {
      return candidate.id === id
    })
  }

  await page.route(FEATURES_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(PLAN_FEATURE_CHOICES) })
  })

  await page.route(OWNER_LANGUAGES_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(OWNER_LANGUAGES) })
  })

  await page.route(RESELLERS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON() as CreateResellerRequest
      requests.created.push(body)
      if (refusals.create !== undefined) {
        await refuse(route, 'ResellerNameTaken', refusals.create)
        return
      }
      const created: ResellerDetail = {
        id: CREATED_RESELLER_ID,
        name: body.name,
        contactEmail: body.contactEmail,
        status: 'active',
        statusName: 'Active',
        createdAt: '2026-11-03T09:00:00+00:00',
        accountCount: 0,
        planCount: 0,
        diskAllocatedMb: 0,
        language: body.language,
        envelope: body.envelope,
      }
      resellers.push(created)
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(resellers, route.request().url(), RESELLER_RULES)),
    })
  })

  await page.route(CHOICES_PATTERN, async (route) => {
    const answer = pagedAnswer(resellers, route.request().url(), CHOICE_RULES)
    const choices: ResellerChoice[] = answer.items.map((reseller) => {
      return { id: reseller.id, name: reseller.name }
    })
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ...answer, items: choices }) })
  })

  await page.route(RESELLER_PATTERN, async (route) => {
    const current = find(route, RESELLER_PATTERN)
    if (current === undefined) {
      await notFound(route)
      return
    }

    if (route.request().method() === 'DELETE') {
      requests.deleted.push(current.id)
      if (refusals.remove !== undefined) {
        await refuse(route, 'ResellerHasAccounts', refusals.remove)
        return
      }
      resellers.splice(resellers.indexOf(current), 1)
      await route.fulfill({ status: 204 })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  await page.route(ENVELOPE_PATTERN, async (route) => {
    const current = find(route, ENVELOPE_PATTERN)
    if (current === undefined) {
      await notFound(route)
      return
    }
    const body = route.request().postDataJSON() as ChangeResellerEnvelopeRequest
    requests.envelopes.push({ id: current.id, body })
    current.envelope = body.envelope
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  await page.route(SUSPEND_PATTERN, async (route) => {
    const current = find(route, SUSPEND_PATTERN)
    if (current === undefined) {
      await notFound(route)
      return
    }
    requests.suspended.push(current.id)
    if (refusals.suspend !== undefined) {
      await refuse(route, 'ResellerSuspended', refusals.suspend)
      return
    }
    current.status = 'suspended'
    current.statusName = 'Suspended'
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  await page.route(REACTIVATE_PATTERN, async (route) => {
    const current = find(route, REACTIVATE_PATTERN)
    if (current === undefined) {
      await notFound(route)
      return
    }
    requests.reactivated.push(current.id)
    if (refusals.reactivate !== undefined) {
      await refuse(route, 'ResellerSuspended', refusals.reactivate)
      return
    }
    current.status = 'active'
    current.statusName = 'Active'
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(current) })
  })

  return requests
}
