import type { Page, Request } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { Account, AccountDetail, AccountOption } from '../../src/types/account'

/** The accounts collection: the paged list (`GET`) and the create form (`POST`), with or without a query. */
const ACCOUNTS_PATTERN = /\/api\/v1\/accounts(\?.*)?$/

/** The account choices pickers read, with or without a query. */
const CHOICES_PATTERN = /\/api\/v1\/accounts\/choices(\?.*)?$/

/** How the accounts endpoint searches, sorts and filters, as the panel does. */
const ACCOUNT_RULES: PagedRules<Account> = {
  matches: (account, term) => {
    return account.name.toLowerCase().includes(term) || account.primaryDomain.toLowerCase().includes(term)
  },
  sorts: {
    name: (account) => {
      return account.name
    },
    status: (account) => {
      return account.status
    },
    createdAt: (account) => {
      return account.createdAt
    },
  },
  filters: {
    planId: (account, value) => {
      return account.planId === value
    },
    status: (account, value) => {
      return account.status === value
    },
  },
}

/** How the choices endpoint searches: by name only, in the catalogue order. */
const CHOICE_RULES: PagedRules<Account> = {
  matches: (account, term) => {
    return account.name.toLowerCase().includes(term)
  },
  sorts: {},
  filters: {},
}

/**
 * Fulfils the accounts endpoints from one catalogue: `GET /api/v1/accounts` as a page honouring
 * `page`, `pageSize`, `search`, `sort`, `planId` and `status`; `GET /api/v1/accounts/choices` as a
 * page of identity and name; and a `POST` by echoing the submitted body back as a created account,
 * which joins the catalogue so the list read after the form's redirect shows it.
 * @param page The Playwright page whose network the route is installed on.
 * @param accounts The accounts the stub knows, in their default order.
 * @returns A function reporting every list request so far, oldest first, so a spec can read what
 * the page asked the panel for.
 */
export const stubAccounts = async (page: Page, accounts: Account[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(ACCOUNTS_PATTERN, async (route) => {
    const request: Request = route.request()
    if (request.method() === 'POST') {
      const submitted = request.postDataJSON() as Pick<Account, 'name' | 'primaryDomain' | 'planId'>
      const created: Account = {
        id: '11111111-1111-1111-1111-111111111111',
        name: submitted.name,
        primaryDomain: submitted.primaryDomain,
        planId: submitted.planId,
        status: 'active',
        createdAt: '2026-08-29T10:00:00Z',
      }
      accounts.push(created)
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(created),
      })
      return
    }

    listRequests.push(new URL(request.url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(accounts, request.url(), ACCOUNT_RULES)),
    })
  })

  await page.route(CHOICES_PATTERN, async (route) => {
    const answer = pagedAnswer(accounts, route.request().url(), CHOICE_RULES)
    const choices: AccountOption[] = answer.items.map((account) => {
      return { id: account.id, name: account.name }
    })
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...answer, items: choices }),
    })
  })

  return () => {
    return listRequests
  }
}

/** One account by its identity: `/api/v1/accounts/{guid}` and nothing below it. */
const ACCOUNT_PATTERN = /\/api\/v1\/accounts\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/

/**
 * Fulfils `GET /api/v1/accounts/{id}` for the given accounts, the read a screen names one account
 * by (the "Only alice" filter strip, the cron screen opened for one account). Kept apart from
 * {@link stubAccounts} because the account detail specs answer this endpoint with their own detail
 * and a catalogue route installed after theirs would take precedence. Any other id, and any other
 * method, falls through to whatever else the spec installed.
 * @param page The Playwright page whose network the route is installed on.
 * @param accounts The accounts the stub names.
 * @returns A function reporting every id read so far, oldest first.
 */
export const stubAccountNames = async (page: Page, accounts: readonly Account[]): Promise<() => readonly string[]> => {
  const reads: string[] = []
  await page.route(ACCOUNT_PATTERN, async (route) => {
    const id = ACCOUNT_PATTERN.exec(new URL(route.request().url()).pathname)?.[1] ?? ''
    const account = accounts.find((candidate) => {
      return candidate.id === id
    })
    if (route.request().method() !== 'GET' || account === undefined) {
      await route.fallback()
      return
    }
    reads.push(id)
    const detail: AccountDetail = { ...account, planDisplayName: 'Starter', ownerEmail: null }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail) })
  })
  return () => {
    return reads
  }
}

/**
 * Fulfils `GET /api/v1/accounts` with an RFC 7807 problem body, so a spec can
 * assert the list page renders the backend's own already-localized message
 * verbatim (rules/vue.md: "the backend owns their text").
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param code The machine-stable problem code the stub reports.
 * @param status HTTP status code the stub responds with.
 * @returns Resolves once the route is installed.
 */
export const stubAccountsProblem = async (
  page: Page,
  detail: string,
  code = 'HostUnexpectedError',
  status = 500,
): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code, title: 'Unexpected error', detail }),
    })
  })
}

/**
 * Serves an empty `GET /api/v1/accounts` page but rejects `POST` with an
 * RFC 7807 problem body, so a spec can assert the create form surfaces the
 * server's own message rather than frontend-invented copy.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @param code The machine-stable problem code the stub reports.
 * @param status HTTP status code the stub responds with.
 * @returns Resolves once the route is installed.
 */
export const stubCreateAccountProblem = async (
  page: Page,
  detail: string,
  code = 'AccountNameTaken',
  status = 409,
): Promise<void> => {
  await page.route(ACCOUNTS_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code, title: 'Conflict', detail }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }),
    })
  })
}
