import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { MailQueueEntry } from '../../src/types/mailQueue'

/** The list endpoint, with or without its query. */
const LIST_PATTERN = /\/api\/v1\/mail-queue(\?.*)?$/

/** What the stubbed queue lets a spec read back. */
export interface MailQueueStub {
  /** Every list request so far, oldest first. */
  lists: () => readonly URL[]
  /** Every message id a removal named, oldest first. */
  removals: () => readonly string[]
  /** How many times a flush was requested. */
  flushes: () => number
}

/** How the stubbed queue answers, besides its rows. */
export interface MailQueueOptions {
  /** Whether the panel says it listed fewer messages than the queue holds. */
  truncated?: boolean
  /** The queue's size when it differs from the rows listed. */
  queueTotal?: number
}

/**
 * How the queue list searches and sorts, as the panel does: the search reads the queue id and the
 * sender, and the sorts are the four the panel whitelists. The numbers are zero-padded so the stub's
 * text comparison orders them as numbers.
 */
const QUEUE_RULES: PagedRules<MailQueueEntry> = {
  matches: (row, term) => {
    return row.queueId.toLowerCase().includes(term) || row.sender.toLowerCase().includes(term)
  },
  sorts: {
    arrivedAt: (row) => {
      return row.arrivedAt
    },
    sender: (row) => {
      return row.sender
    },
    recipients: (row) => {
      return String(row.recipientCount).padStart(10, '0')
    },
    size: (row) => {
      return String(row.sizeBytes).padStart(20, '0')
    },
  },
  filters: {},
}

/**
 * Serves the mail queue the way the panel does: the paged list with its queue total and truncation
 * flag, the count behind the navigation badge, a removal that drops the message, and a flush. A
 * removed message leaves the count too, so a spec can watch the figure follow the queue.
 * @param page The Playwright page whose network the routes are installed on.
 * @param entries The messages the stub holds, in the order the panel lists them by default.
 * @param options How the stub answers besides its rows.
 * @returns A recorder of the list requests, removals and flushes.
 */
export const stubMailQueue = async (
  page: Page,
  entries: MailQueueEntry[],
  options: MailQueueOptions = {},
): Promise<MailQueueStub> => {
  const lists: URL[] = []
  const removals: string[] = []
  let flushes = 0

  await page.route(LIST_PATTERN, async (route) => {
    lists.push(new URL(route.request().url()))
    const messages = pagedAnswer(entries, route.request().url(), QUEUE_RULES)
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        messages,
        queueTotal: options.queueTotal ?? entries.length,
        truncated: options.truncated ?? false,
      }),
    })
  })

  // Registered before the two fixed paths so those win: this pattern also matches `count` and `flush`.
  await page.route('**/api/v1/mail-queue/*', async (route) => {
    const id = new URL(route.request().url()).pathname.split('/').at(-1) ?? ''
    removals.push(id)
    const index = entries.findIndex((entry) => {
      return entry.queueId === id
    })
    if (index >= 0) {
      entries.splice(index, 1)
    }
    await route.fulfill({ status: 204 })
  })

  await page.route('**/api/v1/mail-queue/count', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ count: entries.length }) })
  })

  await page.route('**/api/v1/mail-queue/flush', async (route) => {
    flushes += 1
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ queuedMessages: entries.length }) })
  })

  return {
    lists: (): readonly URL[] => {
      return lists
    },
    removals: (): readonly string[] => {
      return removals
    },
    flushes: (): number => {
      return flushes
    },
  }
}

/**
 * Builds one queued message for a spec, with the fields a spec does not care about filled in.
 * @param queueId The message id.
 * @param sender The envelope sender.
 * @param overrides Any field to set differently.
 * @returns The message.
 */
export const queuedMessage = (queueId: string, sender: string, overrides: Partial<MailQueueEntry> = {}): MailQueueEntry => {
  return {
    queueId,
    queue: 'deferred',
    queueDisplayName: 'Waiting to retry',
    sender,
    recipientCount: 1,
    sizeBytes: 2048,
    arrivedAt: '2026-11-07T08:30:00Z',
    deferralClass: 'network',
    deferralDisplayName: 'Network problem',
    ...overrides,
  }
}
