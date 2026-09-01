import { expect, test } from '@playwright/test'
import { createPinia, setActivePinia } from 'pinia'
import { useSitesStore } from '../../src/stores/sites'

// These run in the Playwright runner's Node process rather than in a browser: the sites data layer
// has no screens until Task 17, and a store is still worth testing before the page that uses it.
// Everything under test is the real production code — the store, the API composable and the
// low-level client's SSE decoder — with only `fetch` replaced, so the stream's ending semantics are
// exercised end to end from the wire bytes up.

const SITE_ID = '11111111-1111-1111-1111-111111111111'

/** How long any wait here may take before the test fails instead of hanging. */
const WAIT_TIMEOUT_MS = 3_000

interface StubbedStream {
  send: (frame: string) => boolean
  close: () => void
  aborted: () => boolean
  opened: Promise<void>
}

const stubSseFetch = (): StubbedStream => {
  const encoder = new TextEncoder()
  let sink: ReadableStreamDefaultController<Uint8Array> | null = null
  let wasAborted = false
  let markOpened = (): void => {}
  const opened = new Promise<void>((resolve) => {
    markOpened = resolve
  })

  const stub = ((_input: string, init: RequestInit): Promise<Response> => {
    const body = new ReadableStream<Uint8Array>({
      start: (controller): void => {
        sink = controller
        markOpened()
      },
    })

    init.signal?.addEventListener('abort', () => {
      wasAborted = true
      // What a real fetch does on abort: the body errors, and the reader's pending read rejects.
      sink?.error(new DOMException('The operation was aborted.', 'AbortError'))
    })

    return Promise.resolve(
      new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
    )
  }) as typeof fetch

  // Assigned through Reflect rather than `globalThis.fetch = …`, which the lint rules forbid
  // outside `useApi.ts`. The ban is aimed at production code reaching the network on its own; a
  // spec replacing the global is how the real client gets exercised without one.
  Reflect.set(globalThis, 'fetch', stub)

  return {
    send: (frame: string): boolean => {
      // A torn-down stream refuses the write, which is itself the observation some tests make:
      // returning false rather than throwing lets a test assert that the panel let go of it.
      try {
        sink?.enqueue(encoder.encode(frame))
        return true
      } catch {
        return false
      }
    },
    close: (): void => {
      sink?.close()
    },
    aborted: (): boolean => {
      return wasAborted
    },
    opened,
  }
}

const within = async <T>(work: Promise<T>, what: string): Promise<T> => {
  let timer: ReturnType<typeof setTimeout> | undefined
  const bound = new Promise<never>((_resolve, reject) => {
    timer = setTimeout(() => {
      reject(new Error(`Timed out after ${WAIT_TIMEOUT_MS}ms waiting for ${what}.`))
    }, WAIT_TIMEOUT_MS)
  })

  try {
    return await Promise.race([work, bound])
  } finally {
    clearTimeout(timer)
  }
}

const eventually = async (condition: () => boolean, what: string): Promise<void> => {
  const deadline = Date.now() + WAIT_TIMEOUT_MS
  while (!condition()) {
    if (Date.now() > deadline) {
      throw new Error(`Timed out after ${WAIT_TIMEOUT_MS}ms waiting for ${what}.`)
    }

    await new Promise((resolve) => {
      setTimeout(resolve, 10)
    })
  }
}

const line = (text: string, historical = false): string => {
  return `event: line\ndata: ${JSON.stringify({ line: text, historical })}\n\n`
}

test.beforeEach(() => {
  setActivePinia(createPinia())
})

test('site log tail delivers every streamed line, in order, split across chunk boundaries', async () => {
  const stream = stubSseFetch()
  const store = useSitesStore()

  const tail = store.startLogTail(SITE_ID, 'access')
  await within(stream.opened, 'the log stream to open')

  // Deliberately cut mid-frame: a chunk boundary falls where TCP put it, not where the protocol did.
  const frames = line('first', true) + line('second')
  stream.send(frames.slice(0, 30))
  stream.send(frames.slice(30))
  stream.send('event: end\ndata: {"reason":"completed"}\n\n')
  stream.close()

  await within(tail, 'the log stream to end')

  expect(store.logLines.map((held) => { return held.line })).toEqual(['first', 'second'])
  expect(store.logLines[0].historical).toBe(true)
  expect(store.logLines[1].historical).toBe(false)
})

test('site log tail keeps the panel’s own ending rather than reporting a generic one', async () => {
  const stream = stubSseFetch()
  const store = useSitesStore()

  const tail = store.startLogTail(SITE_ID, 'error')
  await within(stream.opened, 'the log stream to open')

  stream.send('event: end\ndata: {"reason":"dropped"}\n\n')
  stream.close()
  await within(tail, 'the log stream to end')

  expect(store.logEndReason).toBe('dropped')
  expect(store.logStatus).toBe('ended')
})

test('site log tail reports a stream that closed without naming an ending as truncated', async () => {
  const stream = stubSseFetch()
  const store = useSitesStore()

  const tail = store.startLogTail(SITE_ID, 'access')
  await within(stream.opened, 'the log stream to open')

  stream.send(line('a line nobody was told was the last'))
  stream.close()
  await within(tail, 'the log stream to end')

  expect(store.logEndReason).toBe('truncated')
  expect(store.logEndReason).not.toBe('completed')
})

test('site log tail reports an ending this panel does not recognise as failed, never as completed', async () => {
  const stream = stubSseFetch()
  const store = useSitesStore()

  const tail = store.startLogTail(SITE_ID, 'access')
  await within(stream.opened, 'the log stream to open')

  stream.send('event: end\ndata: {"reason":"somethingNewTheAgentInvented"}\n\n')
  stream.close()
  await within(tail, 'the log stream to end')

  expect(store.logEndReason).toBe('failed')
})

test('stopping a log tail aborts the request, ends as cancelled, and delivers no further lines', async () => {
  const stream = stubSseFetch()
  const store = useSitesStore()

  const tail = store.startLogTail(SITE_ID, 'access')
  await within(stream.opened, 'the log stream to open')
  stream.send(line('before the stop'))
  await eventually(() => {
    return store.logLines.length === 1
  }, 'the first line to arrive')

  store.stopLogTail()
  await within(tail, 'the cancelled stream to settle')

  expect(stream.aborted()).toBe(true)
  expect(store.logEndReason).toBe('cancelled')
  expect(store.logStatus).toBe('ended')

  // Anything sent after the abort must reach nobody: the reader is released, not merely ignored.
  expect(stream.send(line('after the stop'))).toBe(false)
  await new Promise((resolve) => {
    setTimeout(resolve, 50)
  })
  expect(store.logLines.map((held) => { return held.line })).toEqual(['before the stop'])
})

test('starting a second log tail abandons the first, so two logs never interleave', async () => {
  const first = stubSseFetch()
  const store = useSitesStore()

  const firstTail = store.startLogTail(SITE_ID, 'access')
  await within(first.opened, 'the first log stream to open')
  first.send(line('access line'))
  await eventually(() => {
    return store.logLines.length === 1
  }, 'the first stream’s line to arrive')

  const second = stubSseFetch()
  const secondTail = store.startLogTail(SITE_ID, 'error')
  await within(first.opened, 'the first stream to be abandoned')
  await within(second.opened, 'the second log stream to open')

  // The abandoned stream is still capable of pushing; its lines must not join the new log.
  const lateWriteAccepted = first.send(line('a late access line'))
  second.send(line('error line'))
  await eventually(() => {
    return store.logLines.length === 1
  }, 'the second stream’s line to arrive')

  second.send('event: end\ndata: {"reason":"completed"}\n\n')
  second.close()
  await within(secondTail, 'the second stream to end')
  await within(firstTail, 'the first stream to settle')

  expect(first.aborted()).toBe(true)
  expect(lateWriteAccepted).toBe(false)
  expect(store.logLines.map((held) => { return held.line })).toEqual(['error line'])
  expect(store.logEndReason).toBe('completed')
})

test('a line already in flight when a second tail starts is dropped, not appended to the new log', async () => {
  const first = stubSseFetch()
  const store = useSitesStore()

  const firstTail = store.startLogTail(SITE_ID, 'access')
  await within(first.opened, 'the first log stream to open')

  // Enqueued and then abandoned in the same tick: the chunk is already on its way to the reader
  // when the second tail replaces the first, which is the race the stream token exists for.
  first.send(line('a line that was already in flight'))
  const second = stubSseFetch()
  const secondTail = store.startLogTail(SITE_ID, 'error')
  await within(second.opened, 'the second log stream to open')

  second.send('event: end\ndata: {"reason":"completed"}\n\n')
  second.close()
  await within(secondTail, 'the second stream to end')
  await within(firstTail, 'the first stream to settle')

  expect(store.logLines).toEqual([])
})
