import {
  MODULE_BRIDGE_MAX_API_BODY_BYTES,
  MODULE_BRIDGE_MAX_MESSAGE_BYTES,
  MODULE_BRIDGE_MAX_TOAST_LENGTH,
  MODULE_FRAME_MAX_HEIGHT,
  MODULE_FRAME_MIN_HEIGHT,
} from '../../src/types/moduleBridge'
import {
  checkFrameMessage,
  checkReadyMessage,
  clampFrameHeight,
  isApiPathAllowed,
} from '../../src/utils/moduleBridgeMessages'
import { createRateLimiter } from '../../src/utils/moduleBridgeRateLimit'
import { mintBridgeNonce } from '../../src/utils/mintBridgeNonce'
import { expect, test } from '../fixtures/guarded-test'

// The bridge's decisions that need no browser (issue #185): what a frame's message may be, what an
// API path may be, how a height is clamped and how fast a frame may call. They are pure functions
// of their input, so they are exercised here with hostile samples directly, each refusal beside an
// accepted control that proves the check can say yes.

/** The module-relative paths the sample module declares. */
const ROUTES: readonly string[] = ['/hello', '/other']

/** A valid `api` message to mutate. */
const API = { v: 1, type: 'api', id: 'req-1', method: 'GET', path: '/api/v1/hello/greeting' }

test('valid messages of every kind are accepted unchanged', () => {
  expect(checkFrameMessage({ v: 1, type: 'resize', height: 240 }, ROUTES)).toEqual({
    ok: true,
    message: { v: 1, type: 'resize', height: 240 },
  })
  expect(checkFrameMessage({ v: 1, type: 'navigate', path: '/other' }, ROUTES)).toEqual({
    ok: true,
    message: { v: 1, type: 'navigate', path: '/other' },
  })
  expect(checkFrameMessage({ v: 1, type: 'toast', text: 'Saved' }, ROUTES)).toEqual({
    ok: true,
    message: { v: 1, type: 'toast', text: 'Saved' },
  })
  expect(checkFrameMessage(API, ROUTES)).toEqual({ ok: true, message: API })
  expect(checkFrameMessage({ ...API, method: 'POST', body: { name: 'x' } }, ROUTES).ok).toBe(true)
})

test('anything that is not an object with the right version and a string type is malformed', () => {
  for (const hostile of [null, undefined, 7, 'text', [], [{ v: 1, type: 'toast', text: 'x' }], { type: 'toast', text: 'x' }, { v: 2, type: 'toast', text: 'x' }, { v: 1, type: 7 }]) {
    expect(checkFrameMessage(hostile, ROUTES), JSON.stringify(hostile)).toEqual({ ok: false, violation: 'malformed' })
  }
})

test('a cyclic value cannot be serialised and is malformed rather than thrown', () => {
  const cyclic: Record<string, unknown> = { v: 1, type: 'toast', text: 'x' }
  cyclic.self = cyclic
  expect(checkFrameMessage(cyclic, ROUTES)).toEqual({ ok: false, violation: 'malformed' })
})

test('an unknown type, and the types only the shell may send, are refused as unknown', () => {
  for (const type of ['steal-cookie', 'ready', 'init', 'api-result', 'locale', 'theme']) {
    expect(checkFrameMessage({ v: 1, type }, ROUTES), type).toEqual({ ok: false, violation: 'unknown-type' })
  }
})

test('resize takes a whole non-negative number and nothing else', () => {
  for (const height of ['tall', 12.5, -1, Number.NaN, Number.POSITIVE_INFINITY, null]) {
    expect(checkFrameMessage({ v: 1, type: 'resize', height }, ROUTES).ok, String(height)).toBe(false)
  }
  expect(checkFrameMessage({ v: 1, type: 'resize' }, ROUTES).ok).toBe(false)
  expect(checkFrameMessage({ v: 1, type: 'resize', height: 0 }, ROUTES).ok).toBe(true)
})

test('heights are clamped to the shell range, both ways', () => {
  expect(clampFrameHeight(0)).toBe(MODULE_FRAME_MIN_HEIGHT)
  expect(clampFrameHeight(10_000_000)).toBe(MODULE_FRAME_MAX_HEIGHT)
  expect(clampFrameHeight(500)).toBe(500)
  expect(clampFrameHeight(499.6)).toBe(500)
})

test('navigate is accepted only for a declared route', () => {
  expect(checkFrameMessage({ v: 1, type: 'navigate', path: '/elsewhere' }, ROUTES)).toEqual({
    ok: false,
    violation: 'out-of-route',
  })
  expect(checkFrameMessage({ v: 1, type: 'navigate', path: '/hello/' }, ROUTES).ok).toBe(false)
  expect(checkFrameMessage({ v: 1, type: 'navigate', path: 5 }, ROUTES)).toEqual({ ok: false, violation: 'malformed' })
  expect(checkFrameMessage({ v: 1, type: 'navigate', path: '/hello' }, []).ok).toBe(false)
})

test('toast text is 1 to 200 characters and a string', () => {
  const at = (text: unknown): boolean => {
    return checkFrameMessage({ v: 1, type: 'toast', text }, ROUTES).ok
  }
  expect(at('')).toBe(false)
  expect(at(5)).toBe(false)
  expect(at('x'.repeat(MODULE_BRIDGE_MAX_TOAST_LENGTH))).toBe(true)
  expect(at('x'.repeat(MODULE_BRIDGE_MAX_TOAST_LENGTH + 1))).toBe(false)
})

test('a message over 64 KiB is oversized, and exactly under it is not', () => {
  const overhead = JSON.stringify({ v: 1, type: 'toast', text: '' }).length
  const huge = { v: 1, type: 'toast', text: 'x'.repeat(MODULE_BRIDGE_MAX_MESSAGE_BYTES) }
  expect(checkFrameMessage(huge, ROUTES)).toEqual({ ok: false, violation: 'oversized' })
  // Under the size limit the next limit (200 characters) answers instead: malformed, not oversized.
  const under = { v: 1, type: 'toast', text: 'x'.repeat(MODULE_BRIDGE_MAX_MESSAGE_BYTES - overhead) }
  expect(checkFrameMessage(under, ROUTES)).toEqual({ ok: false, violation: 'malformed' })
})

test('an api message is checked field by field', () => {
  const bad = (change: Record<string, unknown>): string => {
    const check = checkFrameMessage({ ...API, ...change }, ROUTES)
    return check.ok ? 'accepted' : check.violation
  }
  expect(bad({ id: '' })).toBe('malformed')
  expect(bad({ id: 'x'.repeat(65) })).toBe('malformed')
  expect(bad({ id: 'has space' })).toBe('malformed')
  expect(bad({ id: 'x'.repeat(64) })).toBe('accepted')
  expect(bad({ method: 'TRACE' })).toBe('malformed')
  expect(bad({ method: 'get' })).toBe('malformed')
  expect(bad({ path: 5 })).toBe('malformed')
  expect(bad({ path: '/health' })).toBe('malformed')
  expect(bad({ path: 'https://evil.example/api/v1/x' })).toBe('malformed')
  expect(bad({ body: { big: 'y'.repeat(MODULE_BRIDGE_MAX_API_BODY_BYTES) } })).toBe('oversized')
  expect(bad({ body: { small: 'y' } })).toBe('accepted')
})

test('an api path must stay inside /api/v1/', () => {
  for (const path of [
    '/api/v1/hello/greeting',
    '/api/v1/hello/items?page=2&size=10',
    '/api/v1/hello/a-b_c.d~e',
  ]) {
    expect(isApiPathAllowed(path), path).toBe(true)
  }
  for (const path of [
    '',
    '/',
    '/api/v1',
    '/api/v2/x',
    'api/v1/x',
    '//evil.example/api/v1/x',
    'https://evil.example/api/v1/x',
    '/api/v1/../admin',
    '/api/v1/a/..%2fb',
    '/api/v1/a/%2e%2e/b',
    '/api/v1/a%5cb',
    '/api/v1//x',
    '/api/v1/a b',
    '/api/v1/a\\b',
    '/api/v1/x#frag',
    '/api/v1/line\nbreak',
    `/api/v1/${'a'.repeat(2000)}`,
  ]) {
    expect(isApiPathAllowed(path), JSON.stringify(path)).toBe(false)
  }
})

test('the handshake message is checked for shape and the nonce is left to the caller', () => {
  expect(checkReadyMessage({ v: 1, type: 'ready', nonce: 'abc' })).toEqual({
    ok: true,
    message: { v: 1, type: 'ready', nonce: 'abc' },
  })
  expect(checkReadyMessage({ v: 1, type: 'ready' })).toEqual({ ok: false, violation: 'malformed' })
  expect(checkReadyMessage({ v: 1, type: 'ready', nonce: 5 })).toEqual({ ok: false, violation: 'malformed' })
  expect(checkReadyMessage({ v: 1, type: 'ready', nonce: '' })).toEqual({ ok: false, violation: 'malformed' })
  expect(checkReadyMessage({ v: 1, type: 'ready', nonce: 'x'.repeat(129) })).toEqual({ ok: false, violation: 'malformed' })
  expect(checkReadyMessage({ v: 1, type: 'resize', nonce: 'abc' })).toEqual({ ok: false, violation: 'unknown-type' })
  expect(checkReadyMessage({ v: 2, type: 'ready', nonce: 'abc' })).toEqual({ ok: false, violation: 'malformed' })
  expect(checkReadyMessage('ready')).toEqual({ ok: false, violation: 'malformed' })
})

test('the rate limiter allows the limit within a window, refuses beyond it and recovers', () => {
  let now = 0
  const allow = createRateLimiter(3, 1000, () => {return now})
  expect([allow(), allow(), allow(), allow(), allow()]).toEqual([true, true, true, false, false])
  now = 999
  expect(allow()).toBe(false)
  now = 1000
  expect(allow()).toBe(true)
  // A refused event is not recorded: only the three accepted ones count against the next window.
  expect([allow(), allow(), allow()]).toEqual([true, true, false])
})

test('a nonce is 32 lowercase hex characters and never repeats', () => {
  const seen = new Set<string>()
  for (let index = 0; index < 50; index++) {
    const nonce = mintBridgeNonce()
    expect(nonce).toMatch(/^[0-9a-f]{32}$/)
    seen.add(nonce)
  }
  expect(seen.size).toBe(50)
})
