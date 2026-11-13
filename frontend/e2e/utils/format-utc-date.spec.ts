import { expect, test } from '@playwright/test'
import { formatDate } from '../../src/utils/formatDate'
import { formatUtcDate } from '../../src/utils/formatUtcDate'

// These run in the Playwright runner's Node process, like the stack unit specs: the real helpers, no browser.
// The licence screen names its expiry as the UTC calendar day, the same day the backend writes in the warning
// sentence and the mail, so the day must not move with the browser's time zone. Every refusal is paired with the
// accepting control that shows the zone really would have moved it.

/**
 * The one corner of Node's `process` these specs write: the time zone. Declared here, not pulled in from
 * `@types/node`, for the reason `e2e/node-builtins.d.ts` gives.
 */
declare const process: { env: Record<string, string | undefined> }

/** The zone the specs run in: ahead of UTC, so a late-evening UTC instant is already the next local day. */
const AHEAD_OF_UTC = 'Asia/Yerevan'

/** An instant that is 26 December in UTC and already 27 December four hours ahead. */
const LATE_EVENING_UTC = '2026-12-26T22:00:00Z'

let originalZone: string | undefined

test.beforeEach(() => {
  originalZone = process.env.TZ
  process.env.TZ = AHEAD_OF_UTC
})

test.afterEach(() => {
  if (originalZone === undefined) {
    delete process.env.TZ
  } else {
    process.env.TZ = originalZone
  }
})

test('the UTC day is shown whatever the browser zone says', () => {
  expect(formatUtcDate(LATE_EVENING_UTC, 'en')).toBe('26 Dec 2026')
})

test('the accepting control: the zone-local formatter moves the same instant to the next day', () => {
  // If this stops being 27 the specs no longer run ahead of UTC and the test above proves nothing.
  expect(formatDate(LATE_EVENING_UTC, 'en')).toBe('27 Dec 2026')
})

test('an instant just after UTC midnight stays on its own UTC day', () => {
  expect(formatUtcDate('2026-12-27T00:30:00Z', 'en')).toBe('27 Dec 2026')
})

test('the day is written in the active language', () => {
  expect(formatUtcDate(LATE_EVENING_UTC, 'ru')).toBe(formatDate('2026-12-26', 'ru'))
  expect(formatUtcDate(LATE_EVENING_UTC, 'hy')).toBe(formatDate('2026-12-26', 'hy'))
})

test('a value that cannot be parsed is returned as it came', () => {
  expect(formatUtcDate('not a date', 'en')).toBe('not a date')
})
