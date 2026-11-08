import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { MailAlias } from '../../src/types/mailAlias'
import type { MailChoices } from '../../src/types/mailChoice'
import type { MailDnsRecord, MailDomain } from '../../src/types/mailDomain'
import type { Mailbox } from '../../src/types/mailbox'

/** The sets and bounds the stubbed panel states, named in English the way the panel names them. */
export const MAIL_CHOICES: MailChoices = {
  domainStatuses: [
    { value: 'active', label: 'Active' },
    { value: 'suspended', label: 'Suspended' },
  ],
  mailboxStatuses: [
    { value: 'active', label: 'Active' },
    { value: 'suspended', label: 'Suspended' },
    { value: 'disabled', label: 'Switched off' },
  ],
  aliasKinds: [
    { value: 'alias', label: 'Alias' },
    { value: 'forwarder', label: 'Forwarder' },
    { value: 'catchAll', label: 'Catch-all' },
  ],
  limits: {
    minimumQuotaMib: 10,
    maximumQuotaMib: 10240,
    defaultQuotaMib: 1024,
    minimumPasswordLength: 10,
    maximumSendLimitPerHour: 500,
    defaultSendLimitPerHour: 100,
    maximumAliasTargets: 3,
  },
}

/** One write the page sent to a mail endpoint: what it was and what it carried. */
export interface MailWrite {
  /** The HTTP method. */
  method: string
  /** The request path, without the query. */
  path: string
  /** The JSON body, or `null` when there was none. */
  body: unknown
}

/** What a collection stub lets a spec read back: every list request and every write. */
export interface MailStubRecorder {
  /** Every list request so far, oldest first. */
  lists: () => readonly URL[]
  /** Every write so far, oldest first. */
  writes: () => readonly MailWrite[]
}

/**
 * Fulfils `GET /api/v1/mail/choices`.
 * @param page The Playwright page whose network the route is installed on.
 * @param choices The sets and bounds the stub reports.
 * @returns Resolves once the route is installed.
 */
export const stubMailChoices = async (page: Page, choices: MailChoices = MAIL_CHOICES): Promise<void> => {
  await page.route('**/api/v1/mail/choices', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(choices) })
  })
}

/**
 * Serves one mail collection the way the panel does: the paged list with its search, sort and
 * filters, a create that echoes the built row into the catalogue, an update that replaces a row, and
 * a delete that drops it. Every write is recorded so a spec can read what the page sent.
 * @param page The Playwright page whose network the route is installed on.
 * @param collection The collection's path under `/api/v1`, e.g. `mailboxes`.
 * @param rows The rows the stub knows, in their default order; created rows join them.
 * @param rules How this endpoint searches, sorts and filters.
 * @param build Builds the row a create answers with from the submitted body.
 * @param change Builds the row an update answers with from the held row and the submitted body.
 * @param writes Where every write is recorded; a caller that installs more routes shares it.
 * @returns A recorder of the list requests and writes.
 */
const stubCollection = async <T extends { id: string }>(
  page: Page,
  collection: string,
  rows: T[],
  rules: PagedRules<T>,
  build: (body: Record<string, unknown>) => T,
  change: (row: T, body: Record<string, unknown>) => T,
  writes: MailWrite[] = [],
): Promise<MailStubRecorder> => {
  const lists: URL[] = []

  await page.route(new RegExp(`/api/v1/${collection}(\\?.*)?$`), async (route) => {
    const request = route.request()
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>
      writes.push({ method: 'POST', path: new URL(request.url()).pathname, body })
      const created = build(body)
      rows.push(created)
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify(created) })
      return
    }
    lists.push(new URL(request.url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(rows, request.url(), rules)),
    })
  })

  await page.route(`**/api/v1/${collection}/*`, async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    const id = path.split('/').at(-1)
    writes.push({ method: request.method(), path, body: request.postData() === null ? null : request.postDataJSON() })
    const index = rows.findIndex((row) => {
      return row.id === id
    })
    if (request.method() === 'DELETE') {
      if (index >= 0) {
        rows.splice(index, 1)
      }
      await route.fulfill({ status: 204 })
      return
    }
    const held = rows[index]
    if (held === undefined) {
      await route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ code: 'NotFound', title: 'Not found', detail: 'Not found.' }) })
      return
    }
    const updated = change(held, request.postDataJSON() as Record<string, unknown>)
    rows[index] = updated
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(updated) })
  })

  return {
    lists: (): readonly URL[] => {
      return lists
    },
    writes: (): readonly MailWrite[] => {
      return writes
    },
  }
}

/**
 * Serves the mail domains endpoint, and the DNS records and DKIM rotation beneath it.
 * @param page The Playwright page whose network the route is installed on.
 * @param domains The domains the stub knows, in their default order.
 * @param records The DNS records every domain answers with.
 * @returns A recorder of the list requests and writes.
 */
export const stubMailDomains = async (
  page: Page,
  domains: MailDomain[],
  records: MailDnsRecord[] = [],
): Promise<MailStubRecorder> => {
  const writes: MailWrite[] = []
  const recorder = await stubCollection<MailDomain>(
    page,
    'mail-domains',
    domains,
    {
      matches: (domain, term) => {
        return domain.name.toLowerCase().includes(term)
      },
      sorts: {
        name: (domain) => {
          return domain.name
        },
        createdAt: (domain) => {
          return domain.createdAt
        },
        status: (domain) => {
          return domain.status
        },
      },
      filters: {
        accountId: (domain, value) => {
          return domain.accountId === value
        },
        status: (domain, value) => {
          return domain.status === value
        },
      },
    },
    (body) => {
      return {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: String(body.accountId),
        ownerName: 'alice',
        name: String(body.name),
        status: 'active',
        statusName: 'Active',
        dkimSelector: 'maran1',
        mailboxCount: 0,
        createdAt: '2026-09-01T10:00:00Z',
      }
    },
    (held) => {
      return held
    },
    writes,
  )

  await page.route('**/api/v1/mail-domains/*/dns-records', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(records) })
  })
  await page.route('**/api/v1/mail-domains/*/dkim-rotation', async (route) => {
    const path = new URL(route.request().url()).pathname
    writes.push({ method: route.request().method(), path, body: null })
    const id = path.split('/').at(-2)
    const held = domains.find((domain) => {
      return domain.id === id
    })
    if (held === undefined) {
      await route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ code: 'NotFound', title: 'Not found', detail: 'Not found.' }) })
      return
    }
    held.dkimSelector = 'maran2'
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(held) })
  })
  return recorder
}

/**
 * Serves the mailboxes endpoint, and the password change beneath it.
 * @param page The Playwright page whose network the route is installed on.
 * @param mailboxes The mailboxes the stub knows, in their default order.
 * @returns A recorder of the list requests and writes.
 */
export const stubMailboxes = async (page: Page, mailboxes: Mailbox[]): Promise<MailStubRecorder> => {
  const writes: MailWrite[] = []
  const recorder = await stubCollection<Mailbox>(
    page,
    'mailboxes',
    mailboxes,
    {
      matches: (mailbox, term) => {
        return mailbox.address.toLowerCase().includes(term)
      },
      sorts: {
        address: (mailbox) => {
          return mailbox.address
        },
        status: (mailbox) => {
          return mailbox.status
        },
      },
      filters: {
        accountId: (mailbox, value) => {
          return mailbox.accountId === value
        },
        domain: (mailbox, value) => {
          return mailbox.address.endsWith(`@${value}`)
        },
        status: (mailbox, value) => {
          return mailbox.status === value
        },
      },
    },
    (body) => {
      return {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: '22222222-2222-2222-2222-222222222222',
        ownerName: 'alice',
        mailDomainId: String(body.mailDomainId),
        address: `${String(body.localPart)}@alpha.example.com`,
        quotaMib: Number(body.quotaMib),
        usedMib: 0,
        sendLimitPerHour: 100,
        status: 'active',
        statusName: 'Active',
        enabled: true,
        createdAt: '2026-09-01T10:00:00Z',
      }
    },
    (held, body) => {
      const enabled = body.enabled === true
      return {
        ...held,
        quotaMib: Number(body.quotaMib),
        sendLimitPerHour: Number(body.sendLimitPerHour),
        status: enabled ? 'active' : 'disabled',
        statusName: enabled ? 'Active' : 'Switched off',
        enabled,
      }
    },
    writes,
  )
  await page.route('**/api/v1/mailboxes/*/password', async (route) => {
    const request = route.request()
    writes.push({ method: request.method(), path: new URL(request.url()).pathname, body: request.postDataJSON() })
    await route.fulfill({ status: 204 })
  })
  return recorder
}

/**
 * Serves the mail aliases endpoint.
 * @param page The Playwright page whose network the route is installed on.
 * @param aliases The aliases the stub knows, in their default order.
 * @returns A recorder of the list requests and writes.
 */
export const stubMailAliases = async (page: Page, aliases: MailAlias[]): Promise<MailStubRecorder> => {
  return await stubCollection<MailAlias>(
    page,
    'mail-aliases',
    aliases,
    {
      matches: (alias, term) => {
        return alias.address.toLowerCase().includes(term)
      },
      sorts: {
        address: (alias) => {
          return alias.address
        },
        kind: (alias) => {
          return alias.kind
        },
      },
      filters: {
        accountId: (alias, value) => {
          return alias.accountId === value
        },
        domain: (alias, value) => {
          return alias.address.endsWith(`@${value}`)
        },
        kind: (alias, value) => {
          return alias.kind === value
        },
      },
    },
    (body) => {
      const kind = body.kind === 'catchAll' || body.kind === 'forwarder' ? body.kind : 'alias'
      return {
        id: '99999999-9999-9999-9999-999999999999',
        accountId: '22222222-2222-2222-2222-222222222222',
        ownerName: 'alice',
        mailDomainId: String(body.mailDomainId),
        address: kind === 'catchAll' ? '*@alpha.example.com' : `${String(body.localPart)}@alpha.example.com`,
        kind,
        kindName: kind === 'catchAll' ? 'Catch-all' : kind === 'forwarder' ? 'Forwarder' : 'Alias',
        targets: body.targets as string[],
        createdAt: '2026-09-01T10:00:00Z',
      }
    },
    (held, body) => {
      return { ...held, targets: body.targets as string[] }
    },
  )
}

/**
 * Makes one method of a mail endpoint answer with an RFC 7807 problem, installed AFTER the stub it
 * overrides so it takes precedence. The backend's own sentence is what a spec asserts the page shows.
 * @param page The Playwright page whose network the route is installed on.
 * @param collection The collection's path under `/api/v1`.
 * @param method The HTTP method to refuse.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubMailRefusal = async (
  page: Page,
  collection: string,
  method: string,
  detail: string,
): Promise<void> => {
  await page.route(new RegExp(`/api/v1/${collection}(\\?.*)?$`), async (route) => {
    if (route.request().method() !== method) {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'MailRefused', title: 'Conflict', detail }),
    })
  })
}
