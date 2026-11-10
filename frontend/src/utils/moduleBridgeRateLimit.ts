/**
 * Builds a sliding-window rate limiter: at most `limit` events within any `windowMs`.
 * @param limit How many events one window allows.
 * @param windowMs The window's length in milliseconds.
 * @param now The clock, injected so the limiter is testable without waiting.
 * @returns A function that records one event and answers whether it was within the limit; an event
 * over the limit is not recorded.
 */
export const createRateLimiter = (limit: number, windowMs: number, now: () => number): (() => boolean) => {
  const stamps: number[] = []

  /**
   * Records one event.
   * @returns True when the event is within the limit.
   */
  const allow = (): boolean => {
    const current = now()
    while (stamps.length > 0 && current - stamps[0] >= windowMs) {
      stamps.shift()
    }
    if (stamps.length >= limit) {
      return false
    }
    stamps.push(current)
    return true
  }

  return allow
}
