import type { RouteLocationRaw } from 'vue-router'
import type { SearchHit } from '../types/search'

/**
 * Maps a search hit to the screen that opens it. The server sends a kind and an id and never a route,
 * so this is the one place that knows which of the SPA's own routes each kind belongs to.
 * @param hit The hit the user chose.
 * @returns The route location to navigate to.
 */
export const searchHitTarget = (hit: SearchHit): RouteLocationRaw => {
  switch (hit.kind) {
    case 'account':
      return { name: 'account-detail', params: { id: hit.id } }
    case 'site':
      return { name: 'site-detail', params: { id: hit.id } }
  }
}
