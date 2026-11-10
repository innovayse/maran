import type { Page, Request, Response } from '@playwright/test'
import { expectationFor, findOperation } from './openapi-baseline'
import { checkAgainstSchema } from './openapi-schema-check'

/**
 * Which lane the guard watches.
 *
 * - `stubbed`: every API request must be answered by a stub the spec installed. A request that
 *   reaches no stub is recorded and refused, because an unanswered read reaches no backend and the
 *   page then draws whatever it draws for a failure.
 * - `real`: the requests go to a running panel. A network-level failure of an API request is
 *   recorded, since there is no stub to blame.
 *
 * In both lanes a 4xx/5xx the spec did not announce, a console error, an uncaught page error and a
 * response body that disagrees with `docs/api/openapi-baseline.json` are recorded.
 */
export type GuardMode = 'stubbed' | 'real'

/** What a spec holds to tell the guard about an expected refusal and to read its findings. */
export interface NetworkGuard {
  /**
   * Announces that a response with this status for a matching URL is the point of the test, so the
   * guard does not count it.
   * @param status The status that is expected.
   * @param urlPattern Matches the request URL the status is expected on.
   */
  allow: (status: number, urlPattern: RegExp) => void
  /**
   * Waits for the body checks still running and returns every finding so far.
   * @returns One sentence per finding; empty when the page behaved.
   */
  problems: () => Promise<string[]>
}

/** An announced refusal. */
interface AllowedRefusal {
  /** The expected status. */
  status: number
  /** Matches the URL it is expected on. */
  urlPattern: RegExp
}

/** The prefix of every request the guard watches. */
const API_PREFIX = '/api/'

/** Chromium's console line for a failed request; the response or route finding already names it. */
const RESOURCE_FAILURE = /^Failed to load resource/

/**
 * Installs the guard on a page. Call it before the spec installs its own stubs: the catch-all that
 * finds unstubbed requests sits on the browser context, which Playwright consults only after the
 * page's own routes, so a stub the spec adds later still wins.
 * @param page The Playwright page under test.
 * @param mode The lane the guard watches.
 * @returns The handle the spec uses to announce expected refusals and read the findings.
 */
export const installNetworkGuard = async (page: Page, mode: GuardMode): Promise<NetworkGuard> => {
  const findings: string[] = []
  const allowed: AllowedRefusal[] = []
  const pending: Promise<void>[] = []
  // Resolved when the findings are asked for: a body whose request has not finished by then is not
  // part of what the test observed, and waiting for it would hold the teardown for as long as the
  // request lives (a poll, a held read, a connection the page keeps open).
  let closeGate: () => void = (): void => {
    // Replaced below, before the first response can wait on it.
  }
  const closing = new Promise<void>((resolve) => {
    closeGate = resolve
  })
  // Requests that completed, and the waiters of those that have not yet.
  const finishedRequests = new Set<Request>()
  const finishWaiters = new Map<Request, () => void>()

  /**
   * Whether the spec announced this refusal.
   * @param status The response status.
   * @param url The request URL.
   * @returns True when it was announced.
   */
  const isAllowed = (status: number, url: string): boolean => {
    return allowed.some((entry) => {return entry.status === status && entry.urlPattern.test(url)})
  }

  /**
   * Checks a response body against the documented shape of its status.
   * @param response The response whose body is read.
   * @returns Resolves once the body has been judged.
   */
  const checkContract = async (response: Response): Promise<void> => {
    const status = response.status()
    // `headers()` is the set the response arrived with. `headerValue()` waits for the browser's raw
    // header event, which a response answered by a route handler does not always send, so awaiting
    // it could outlive the test.
    const contentType = response.headers()['content-type'] ?? ''
    if (status === 204 || status === 304 || !contentType.includes('json')) {
      return
    }
    const request = response.request()
    // A framed module's own route (the shell relays it with `X-Maran-Frame`) belongs to that module's
    // contract, shipped in its package, and is not an operation of the panel's baseline (issue #185).
    if (request.headers()['x-maran-frame'] !== undefined) {
      return
    }
    const pathname = new URL(response.url()).pathname
    const label = `${request.method()} ${pathname} -> ${status.toString()}`
    const found = findOperation(request.method(), pathname)
    if (found === null) {
      findings.push(`contract: ${label} is not an operation of docs/api/openapi-baseline.json`)
      return
    }
    const expectation = expectationFor(found.operation, status)
    if (expectation.kind === 'undocumented' && status < 400) {
      findings.push(`contract: ${label} is a status ${found.template} does not document`)
      return
    }
    if (expectation.kind !== 'schema') {
      return
    }
    // Reading the body and parsing it are two different failures. A body the browser has already
    // dropped (the page navigated away, or the context closed, while the shell's polled read was in
    // flight) makes `text()` throw; that rejection leaves this function, and the caller treats an
    // unreadable body as no finding. Only text that was read and does not parse is a finding.
    // `text()` waits for the request to finish. Wait for that too, but give up when the findings are
    // asked for: an unfinished body is unobserved, not a defect of the page.
    if (!finishedRequests.has(request)) {
      const finished = new Promise<void>((resolve) => {
        finishWaiters.set(request, resolve)
      })
      await Promise.race([finished, closing])
      if (!finishedRequests.has(request)) {
        return
      }
    }
    const text = await response.text()
    let body: unknown
    try {
      body = JSON.parse(text)
    } catch {
      findings.push(`contract: ${label} claims JSON but the body does not parse`)
      return
    }
    for (const finding of checkAgainstSchema(expectation.schema, body, '$')) {
      findings.push(`contract: ${label} ${finding}`)
    }
  }

  page.on('response', (response) => {
    const url = new URL(response.url())
    if (!url.pathname.startsWith(API_PREFIX)) {
      return
    }
    const status = response.status()
    if (status >= 400 && !isAllowed(status, response.url())) {
      findings.push(`unintended ${status.toString()}: ${response.request().method()} ${url.pathname}`)
    }
    // A body that cannot be read (the page navigated away mid-response) is not a finding.
    pending.push(checkContract(response).catch(() => {return undefined}))
  })

  page.on('requestfinished', (request) => {
    finishedRequests.add(request)
    finishWaiters.get(request)?.()
  })

  page.on('console', (message) => {
    if (message.type() === 'error' && !RESOURCE_FAILURE.test(message.text())) {
      findings.push(`console error: ${message.text()}`)
    }
  })

  page.on('pageerror', (error) => {
    findings.push(`uncaught page error: ${error.message}`)
  })

  if (mode === 'stubbed') {
    await page.context().route(/\/api\//, async (route) => {
      findings.push(`unstubbed request: ${route.request().method()} ${new URL(route.request().url()).pathname}`)
      await route.abort('failed')
    })
  } else {
    page.on('requestfailed', (request) => {
      const url = new URL(request.url())
      // A navigation cancels the reads of the page it leaves; that is the browser, not the panel.
      if (url.pathname.startsWith(API_PREFIX) && request.failure()?.errorText !== 'net::ERR_ABORTED') {
        findings.push(`failed request: ${request.method()} ${url.pathname} (${request.failure()?.errorText ?? 'unknown'})`)
      }
    })
  }

  return {
    allow: (status, urlPattern): void => {
      allowed.push({ status, urlPattern })
    },
    problems: async (): Promise<string[]> => {
      // A round trip to the page delivers every event the browser already sent, so a request that
      // finished before this call is counted as finished and only a live one is given up on.
      await page.evaluate(() => {return undefined}).catch(() => {return undefined})
      closeGate()
      await Promise.all(pending)
      return [...findings]
    },
  }
}
