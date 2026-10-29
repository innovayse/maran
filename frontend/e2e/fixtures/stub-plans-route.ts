import type { Page, Route } from '@playwright/test'
import type { Plan } from '../../src/types/account'
import type { ManagedPlan, PlanRequest } from '../../src/types/plan'

/**
 * The single plan the create-account form offers in specs. One is enough: the form's
 * subject is what it does with a chosen plan, not how a list of them is ordered.
 */
export const THE_PLAN: Plan = {
  id: '22222222-2222-2222-2222-222222222222',
  displayName: 'Starter',
  diskQuotaMb: 5120,
  maxSites: 5,
  maxDatabases: 5,
  maxSftpUsers: 5,
}

/**
 * Fulfils `GET /api/v1/accounts/plans` so the form's plan picker has something to offer.
 *
 * The picker exists because a plan id is server-owned reference data: the form asks the
 * backend what may be chosen rather than letting a person type an identifier
 * (rules/architecture.md).
 * @param page The Playwright page whose network the route is installed on.
 * @param plans The plans the panel reports; the single default plan when omitted.
 * @returns Resolves once the route is installed.
 */
export const stubPlans = async (page: Page, plans: Plan[] = [THE_PLAN]): Promise<void> => {
  await page.route('**/api/v1/accounts/plans', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(plans),
    })
  })
}

/**
 * The three standard plans a fresh panel seeds, so a spec can drive a picker with more than one
 * choice in it.
 *
 * Three and not two: the option that could not be clicked was the LAST one, clipped by the card
 * the form is drawn in, and a two-option list is short enough to fit inside the clip.
 */
export const THREE_PLANS: Plan[] = [
  THE_PLAN,
  {
    id: '33333333-3333-3333-3333-333333333333',
    displayName: 'Business',
    diskQuotaMb: 25600,
    maxSites: 25,
    maxDatabases: 10,
    maxSftpUsers: 10,
  },
  {
    id: '44444444-4444-4444-4444-444444444444',
    displayName: 'Unlimited',
    diskQuotaMb: 1048576,
    maxSites: 500,
    maxDatabases: 500,
    maxSftpUsers: 100,
  },
]

/**
 * The plans the management screen lists in specs: a shipped-looking tier with accounts on it
 * (which cannot be deleted), one nobody is on (which can), and a tier that sells no FTPS logins and
 * no cron entries, so the word "None" has a row to appear in.
 */
export const MANAGED_PLANS: ManagedPlan[] = [
  {
    id: '22222222-2222-2222-2222-222222222222',
    displayName: 'Starter',
    diskQuotaMb: 5120,
    maxSites: 5,
    maxDatabases: 2,
    maxSftpUsers: 3,
    maxFtpUsers: 0,
    maxCronEntries: 0,
    maxPhpWorkersPerPool: 5,
    accountCount: 3,
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    displayName: 'Business',
    diskQuotaMb: 25600,
    maxSites: 25,
    maxDatabases: 10,
    maxSftpUsers: 10,
    maxFtpUsers: 10,
    maxCronEntries: 20,
    maxPhpWorkersPerPool: 10,
    accountCount: 0,
  },
  {
    id: '44444444-4444-4444-4444-444444444444',
    displayName: 'Unlimited',
    diskQuotaMb: 1048576,
    maxSites: 500,
    maxDatabases: 500,
    maxSftpUsers: 100,
    maxFtpUsers: 100,
    maxCronEntries: 200,
    maxPhpWorkersPerPool: 20,
    accountCount: 1,
  },
]

/** What a management stub records of the requests it served, so a spec can assert what was sent. */
export interface PlanRequests {
  /** The body of every `POST /api/v1/plans`, in order. */
  created: PlanRequest[]
  /** The path id and body of every `PUT /api/v1/plans/{id}`, in order. */
  updated: { id: string; body: PlanRequest }[]
  /** The id of every `DELETE /api/v1/plans/{id}`, in order. */
  deleted: string[]
}

/**
 * The server's refusals a spec can make a management stub answer with, each as the RFC 7807
 * problem the real panel sends. Text is the spec's, so a spec can prove it is rendered verbatim.
 */
export interface PlanRefusals {
  /** Refuses every `POST`, as the real panel does for a name another plan has. */
  create?: string
  /** Refuses every `PUT`, as the real panel does for a quota an account already exceeds. */
  update?: string
  /** Refuses every `DELETE`, as the real panel does for a plan accounts are still on. */
  remove?: string
}

/**
 * Fulfils the plan-management endpoints: `GET` and `POST` on `/api/v1/plans`, `PUT` and `DELETE`
 * on `/api/v1/plans/{id}`. The list is held in memory and changes as the page changes it, the way
 * the server's does, so the list a form returns to shows the result.
 * @param page The Playwright page whose network the route is installed on.
 * @param plans The plans the panel starts with; mutated by successful writes.
 * @param refusals Writes to refuse with a problem body instead of performing them.
 * @returns What the stub received, filled in as the page makes requests.
 */
export const stubManagedPlans = async (
  page: Page,
  plans: ManagedPlan[] = MANAGED_PLANS.map((plan) => {
    return { ...plan }
  }),
  refusals: PlanRefusals = {},
): Promise<PlanRequests> => {
  const requests: PlanRequests = { created: [], updated: [], deleted: [] }

  /**
   * Answers with a refusal the way the panel does.
   * @param route The intercepted request.
   * @param detail The message to put in the problem body.
   * @returns Resolves once the response is sent.
   */
  const refuse = async (route: Route, detail: string): Promise<void> => {
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'PlanConflict', title: 'Conflict', detail }),
    })
  }

  await page.route('**/api/v1/plans', async (route) => {
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON() as PlanRequest
      requests.created.push(body)
      if (refusals.create !== undefined) {
        await refuse(route, refusals.create)
        return
      }
      const created: ManagedPlan = {
        id: '55555555-5555-5555-5555-555555555555',
        displayName: body.name,
        diskQuotaMb: body.diskQuotaMb,
        maxSites: body.maxSites,
        maxDatabases: body.maxDatabases,
        maxSftpUsers: body.maxSftpUsers,
        maxFtpUsers: body.maxFtpUsers,
        maxCronEntries: body.maxCronEntries,
        maxPhpWorkersPerPool: body.maxPhpWorkersPerPool,
        accountCount: 0,
      }
      plans.push(created)
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) })
      return
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(plans) })
  })

  await page.route('**/api/v1/plans/*', async (route) => {
    const id = route.request().url().split('/').pop() ?? ''
    const index = plans.findIndex((plan) => {
      return plan.id === id
    })
    const current = plans[index]

    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON() as PlanRequest
      requests.updated.push({ id, body })
      if (refusals.update !== undefined) {
        await refuse(route, refusals.update)
        return
      }
      if (current === undefined) {
        await route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ code: 'PlanNotFound', title: 'Not found', detail: 'No such plan.' }) })
        return
      }
      const updated: ManagedPlan = { ...current, ...body, displayName: body.name }
      plans[index] = updated
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(updated) })
      return
    }

    if (route.request().method() === 'DELETE') {
      requests.deleted.push(id)
      if (refusals.remove !== undefined) {
        await refuse(route, refusals.remove)
        return
      }
      plans.splice(index, 1)
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'true' })
      return
    }

    await route.fallback()
  })

  return requests
}
