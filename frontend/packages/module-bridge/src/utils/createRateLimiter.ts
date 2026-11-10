/**
 * Makes a sliding-window limiter: at most `limit` permits in any window of `windowMs`.
 *
 * @param limit The most permits per window.
 * @param windowMs The window length in milliseconds.
 * @param now A monotonic clock in milliseconds.
 * @returns A function that returns true and records a permit when one is free, false when the window is full.
 */
export const createRateLimiter = (limit: number, windowMs: number, now: () => number): (() => boolean) => {
  /** The times, in clock milliseconds, of the permits granted inside the current window. */
  const granted: number[] = []

  /**
   * Takes one permit if the window has room.
   * @returns `true` when a permit was granted, `false` when the window is full.
   */
  return (): boolean => {
    const current = now()

    while (granted.length > 0 && current - (granted[0] ?? current) >= windowMs) {
      granted.shift()
    }

    if (granted.length >= limit) {
      return false
    }

    granted.push(current)
    return true
  }
}
