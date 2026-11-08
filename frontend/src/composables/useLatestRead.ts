/** Answers whether the read it was issued for is still the newest one begun. */
export type IsLatestRead = () => boolean

/** The latest-read-wins guard of one list, as {@link useLatestRead} returns it. */
export interface LatestRead {
  /**
   * Starts a read and supersedes every read begun before it.
   * @returns A check to ask once the read's answer arrives: true only while no newer read has begun.
   */
  begin: () => IsLatestRead
}

/**
 * The one latest-read-wins guard of the SPA. Two reads of the same list can be in flight at once
 * (a search typed twice, a page changed while the last one is still loading), and the network may
 * answer them out of order. A store that assigns its rows the moment a read answers lets the slow
 * older read overwrite the newer one, so the screen shows rows that no longer match the search or
 * page in the URL. A store makes one guard per list, calls {@link LatestRead.begin} before it
 * asks, and assigns what came back only while the returned check still says true.
 *
 * It holds a counter and nothing else, so it is safe to call from a store or a composable.
 * @returns The guard for one list.
 */
export const useLatestRead = (): LatestRead => {
  /** Sequence number of the newest read begun; an answer carrying an older number is stale. */
  let newest = 0

  /**
   * Starts a read and supersedes every read begun before it.
   * @returns A check that is true only while this read is still the newest.
   */
  const begin = (): IsLatestRead => {
    newest += 1
    const mine = newest
    return (): boolean => {return mine === newest}
  }

  return { begin }
}
