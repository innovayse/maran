import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFavouritesApi } from '../composables/apis/useFavouritesApi'
import { ApiError } from '../composables/useApi'
import type { Favourites, FavouritePage } from '../types/favourite'
import type { LoadingState } from '../types/loading'

/**
 * Owns the signed-in person's pinned pages. The server keeps the keys, per user; this store keeps
 * the last list it was told and answers "is this page pinned" for the sidebar, the drawer and the
 * app bar's favourites menu. Components never touch the API composable (rules/vue.md).
 */
export const useFavouritesStore = defineStore('favourites', () => {
  const api = useFavouritesApi()

  /** The pins as last reported, lowest position first. */
  const items: Ref<FavouritePage[]> = ref([])

  /** The most pages one person may pin as the panel reported it, or null before the first answer. */
  const limit: Ref<number | null> = ref(null)

  /** Where the last read stands. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the last refused read or change, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * The user whose pins are held, or null when none are asked for yet. Kept so that a second person
   * signing in on the same tab never sees the first one's pins while their own are on the way.
   */
  const ownerId: Ref<string | null> = ref(null)

  /** True while a pin or unpin is on its way; the toggles ignore presses until it settles. */
  const changing: Ref<boolean> = ref(false)

  /** The pinned keys, in the order the list is drawn. */
  const keys: ComputedRef<string[]> = computed(() => {
    return items.value.map((item): string => {
      return item.itemKey
    })
  })

  /** Whether the person already holds as many pins as the panel allows. Advice: the server decides. */
  const isFull: ComputedRef<boolean> = computed(() => {
    return limit.value !== null && items.value.length >= limit.value
  })

  /**
   * Replaces the held list with one the panel reported.
   * @param favourites The pins and the limit.
   * @returns Nothing.
   */
  const apply = (favourites: Favourites): void => {
    items.value = favourites.items
    limit.value = favourites.limit
  }

  /**
   * Reads the caller's pins.
   *
   * Failures are captured into `errorMessage` rather than thrown: the shell renders without
   * favourites, and a pin list that cannot be read is a display state.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    errorMessage.value = null
    try {
      apply(await api.list())
      loadState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
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
    items.value = []
    limit.value = null
    await load()
  }

  /**
   * Forgets the held pins, for sign-out.
   * @returns Nothing.
   */
  const clear = (): void => {
    ownerId.value = null
    items.value = []
    limit.value = null
    loadState.value = 'loading'
    errorMessage.value = null
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
    if (changing.value) {
      return false
    }
    changing.value = true
    errorMessage.value = null
    try {
      apply(isPinned(itemKey) ? await api.unpin(itemKey) : await api.pin({ itemKey }))
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      changing.value = false
    }
  }

  return { items, limit, loadState, errorMessage, changing, keys, isFull, ensureLoaded, clear, isPinned, toggle }
})
