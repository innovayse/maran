import { onScopeDispose, readonly, ref, type Ref } from 'vue'

/**
 * Tracks whether a CSS media query matches, as a ref that follows the viewport.
 *
 * For a component that must render a different STRUCTURE at a breakpoint (a bottom sheet below
 * `lg`, a centred dialog above it), which CSS alone cannot do. A component that only needs a
 * different look uses Tailwind's responsive prefixes instead.
 *
 * The listener is removed when the calling scope is disposed, so a component that unmounts leaves
 * nothing attached to the window.
 * @param query A media query, e.g. `(min-width: 64rem)`.
 * @returns A read-only ref that is true while the query matches.
 */
export const useMediaQuery = (query: string): Readonly<Ref<boolean>> => {
  const list = window.matchMedia(query)
  const matches: Ref<boolean> = ref(list.matches)

  /**
   * Copies the query's new state into the ref.
   * @param event The change event the media query list fired.
   * @returns Nothing.
   */
  const onChange = (event: MediaQueryListEvent): void => {
    matches.value = event.matches
  }

  list.addEventListener('change', onChange)
  onScopeDispose((): void => {
    list.removeEventListener('change', onChange)
  })

  return readonly(matches)
}
