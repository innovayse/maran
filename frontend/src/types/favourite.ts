/**
 * The pages a person pinned to the app bar (rules/vue.md "Types"). The server stores only the
 * navigation tree's item keys, per user; which of them are drawn is decided here, against what the
 * signed-in person is offered now.
 */

/** One pinned page, as `GET /api/v1/me/pinned-pages` reports it. */
export interface FavouritePage {
  /** The key of the page in `router/navigationTree.ts`. */
  itemKey: string
  /** Where the pin sits in the person's list, lowest first. */
  position: number
}

/** The person's pins and how many they may keep. */
export interface Favourites {
  /** The pins, lowest position first. */
  items: FavouritePage[]
  /** The most pages one person may pin, as the panel reports it. */
  limit: number
}

/** The body of `POST /api/v1/me/pinned-pages`. */
export interface PinPageRequest {
  /** The key of the page to pin. */
  itemKey: string
}

/** Public surface of the favourites API composable; every call answers with the list after it. */
export interface FavouritesApi {
  /** Reads the caller's pins. */
  list: (signal?: AbortSignal) => Promise<Favourites>
  /** Pins a page. */
  pin: (request: PinPageRequest, signal?: AbortSignal) => Promise<Favourites>
  /** Removes a page from the pins. */
  unpin: (itemKey: string, signal?: AbortSignal) => Promise<Favourites>
}
