import type { ListRequest, Paged } from './list'

/**
 * The queue a message waits in, as the panel names it on the wire. The text a person reads is the
 * row's `queueDisplayName`.
 */
export type MailQueueName = 'active' | 'deferred' | 'held' | 'incoming'

/**
 * The class of reason a message waits, never the reason's own text. The text a person reads is the
 * row's `deferralDisplayName`.
 */
export type MailDeferralClass = 'none' | 'network' | 'remoteRefused' | 'localError' | 'other'

/**
 * Outward view of one message waiting in the host's mail queue, mirroring the backend's
 * `MailQueueEntryDto` field-for-field.
 */
export interface MailQueueEntry {
  /** The mail transfer agent's id for the message; the handle a removal names it by. */
  queueId: string
  /** The queue the message waits in. */
  queue: MailQueueName
  /** The queue as the caller reads it, in their language, produced by the panel. */
  queueDisplayName: string
  /**
   * The envelope sender. UNTRUSTED text: it came from whoever sent the message, and it is drawn as
   * text and never as markup or a link.
   */
  sender: string
  /** How many recipients the message still has. */
  recipientCount: number
  /** The message size in bytes. */
  sizeBytes: number
  /** When the message was accepted, as an ISO-8601 instant. */
  arrivedAt: string
  /** The class of reason the message waits. */
  deferralClass: MailDeferralClass
  /** The class of reason as the caller reads it, in their language, produced by the panel. */
  deferralDisplayName: string
}

/**
 * One page of the host's mail queue, mirroring the backend's `MailQueueDto`.
 */
export interface MailQueue {
  /** The requested page of the listed messages that matched the search. */
  messages: Paged<MailQueueEntry>
  /** How many messages the queue holds in all, listed or not. */
  queueTotal: number
  /** Whether the panel listed fewer messages than the queue holds, so pages and search cover the newest only. */
  truncated: boolean
}

/** The queue size after a flush, mirroring the backend's `MailQueueFlushDto`. */
export interface MailQueueFlush {
  /** How many messages were queued after the flush was requested, or `null` when the panel did not read it. */
  queuedMessages: number | null
}

/** How many messages are queued, mirroring the backend's `MailQueueCountDto`. */
export interface MailQueueCount {
  /** The number of queued messages, across every queue the mail transfer agent keeps. */
  count: number
}

/** The mail queue endpoints this SPA calls, as `useMailQueueApi` implements them. */
export interface MailQueueApi {
  /**
   * Reads one page of the queue.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page, the queue's total and whether the list is truncated.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<MailQueue>

  /**
   * Reads how many messages the queue holds, the figure behind the navigation badge.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count.
   */
  count: (signal?: AbortSignal) => Promise<MailQueueCount>

  /**
   * Removes one queued message.
   * @param queueId The mail transfer agent's id for the message.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed it; the answer is an empty 204.
   */
  remove: (queueId: string, signal?: AbortSignal) => Promise<void>

  /**
   * Asks the mail transfer agent to retry every deferred message now.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The queue size after the request.
   */
  flush: (signal?: AbortSignal) => Promise<MailQueueFlush>
}
