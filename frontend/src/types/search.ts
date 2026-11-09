/** What the global search finds; mirrors the backend's `SearchKind`, camelCase on the wire. */
export type SearchKind = 'account' | 'site'

/** Whether one provider's group could be produced; mirrors the backend's `SearchGroupState`. */
export type SearchGroupState = 'ready' | 'unavailable'

/**
 * One thing a search found. The server sends the kind and the id, never a route: the SPA maps them to
 * its own screens.
 */
export interface SearchHit {
  /** What was found. */
  kind: SearchKind
  /** The thing's identity, as the screen that opens it addresses it. */
  id: string
  /** The main line, usually the thing's name. */
  title: string
  /** A second line saying which field matched or what the thing is; empty when there is none. */
  subtitle: string
}

/** One provider's part of the answer. */
export interface SearchGroup {
  /** What the hits are. */
  kind: SearchKind
  /** Whether the provider could answer; `hits` is empty unless it is `ready`. */
  state: SearchGroupState
  /** The hits, best first. */
  hits: SearchHit[]
}

/** The answer to `POST /api/v1/search`. */
export interface SearchResult {
  /** One group per provider the server asked. */
  groups: SearchGroup[]
}

/** The typed calls the global search needs. */
export interface SearchApi {
  /**
   * Asks the panel what matches a term. The term travels in the request body so it never reaches a URL.
   * @param term The text typed, at least the server's minimum length.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The groups the panel answered with.
   */
  search: (term: string, signal?: AbortSignal) => Promise<SearchResult>
}
