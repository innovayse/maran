import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFavouritesApi } from '../composables/apis/useFavouritesApi'
import { ApiError } from '../composables/useApi'
import type { Favourites, FavouritePage } from '../types/favourite'
import type { Read, Submission } from '../types/loading'

/**
 * Owns the signed-in person's pinned pages. The server keeps the keys, per user; this store keeps
 * the last list it was told and answers "is this page pinned" for the sidebar, the drawer and the
 * app bar's favourites menu. Components never touch the API composable (rules/vue.md).
 */
export const useFavouritesStore = defineStore('favourites', () => {
  const api = useFavouritesApi()

  /**
   * The pins and the limit as the panel last reported them. A pin list nobody has asked for is
   * `idle`, and one that is on its way is `loading`, so the limit is never a guessed number or a
   * `null` standing in for "not answered yet".
   */
  const read: Ref<Read<Favourites>> = ref({ state: 'idle' })

  /** Where the last pin or unpin stands; the toggles ignore presses while it is `sending`. */
  const change: Ref<Submission> = ref({ state: 'idle' })

  /**
   * The user whose pins are held, or null when none are asked for yet. Kept so that a second person
   * signing in on the same tab never sees the first one's pins while their own are on the way.
   */
  const ownerId: Ref<string | null> = ref(null)

  /** The pins as last reported, lowest position first; none until the panel has answered. */
  const items: ComputedRef<FavouritePage[]> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value.items : []
  })

  /** Backend-localized message from the last refused read or change, rendered verbatim. */
  const errorMessage: ComputedRef<string | null> = computed(() => {
    if (read.value.state === 'failed') {
      return read.value.message
    }

    return change.value.state === 'failed' ? change.value.message : null
  })

  /** True while a pin or unpin is on its way. */
  const changing: ComputedRef<boolean> = computed(() => {
    return change.value.state === 'sending'
  })

  /** The pinned keys, in the order the list is drawn. */
  const keys: ComputedRef<string[]> = computed(() => {
    return items.value.map((item): string => {
      return item.itemKey
    })
  })

  /** Whether the person already holds as many pins as the panel allows. Advice: the server decides. */
  const isFull: ComputedRef<boolean> = computed(() => {
    return read.value.state === 'loaded' && read.value.value.items.length >= read.value.value.limit
  })

  /**
   * Reads the caller's pins.
   *
   * Failures are captured into the read rather than thrown: the shell renders without
   * favourites, and a pin list that cannot be read is a display state.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    change.value = { state: 'idle' }
    try {
      read.value = { state: 'loaded', value: await api.list() }
    } catch (error) {
      read.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
    }
  }

  /**
   * Reads the pins of a person unless they are already held (or on their way). Called by every
   * component that draws a pin, so whichever mounts first reads and the rest find it done.
   * @param userId The signed-in person's id.
   * @returns Resolves once the read settled; at once when it was not needed.
   */
  const ensureLoaded = async (userId: string): Promise<void> => {
    if (ownerId.value === userId) {
      return
    }
    // Claimed before the request is awaited, so a second caller in the same tick sees it taken; the
    // previous person's pins are dropped now, not when the answer arrives.
    ownerId.value = userId
    read.value = { state: 'idle' }
    await load()
  }

  /**
   * Forgets the held pins, for sign-out.
   * @returns Nothing.
   */
  const clear = (): void => {
    ownerId.value = null
    read.value = { state: 'idle' }
    change.value = { state: 'idle' }
  }

  /**
   * Reports whether a page is pinned.
   * @param itemKey The page's navigation key.
   * @returns True when it is among the held pins.
   */
  const isPinned = (itemKey: string): boolean => {
    return keys.value.includes(itemKey)
  }

  /**
   * Pins a page when it is not pinned, and unpins it when it is. The held list becomes whatever the
   * panel answers with, so the server's limit and ordering are never second-guessed here.
   * @param itemKey The page's navigation key.
   * @returns True when the change was made; false when the panel refused it (the reason is in `errorMessage`).
   */
  const toggle = async (itemKey: string): Promise<boolean> => {
    if (change.value.state === 'sending') {
      return false
    }
    change.value = { state: 'sending' }
    try {
      read.value = { state: 'loaded', value: isPinned(itemKey) ? await api.unpin(itemKey) : await api.pin({ itemKey }) }
      change.value = { state: 'done' }
      return true
    } catch (error) {
      change.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
  }

  return { items, errorMessage, changing, keys, isFull, ensureLoaded, clear, isPinned, toggle }
})
