import { nextTick, onBeforeUnmount, onMounted, watch, type Ref } from 'vue'
import { useRoute } from 'vue-router'
import type { NavigationEntry } from '../types/navigation'

/**
 * Keeps the current navigation row visible inside its scroller (issue #221): on a short window
 * the lit child of a lower section ("Backup list") sat hidden under the identity card.
 *
 * It runs on every navigation and whenever the entries change, because on a first load the
 * catalogue can arrive after the route has settled, and the row only exists once it has. The
 * wait for the next tick lets a section that just opened render its children first. The list
 * moves only as far as needed (the nearest edge), and not at all when the row is already
 * visible, so navigating between visible rows never makes it jump.
 *
 * It also runs when the scroller itself changes height. The identity card below the list fills in
 * after the first paint, which shortens the scroller AFTER the row was revealed and left a lit
 * row half under the card on a deep link (issue #311 measured 20px on a 480px window).
 *
 * The scroller is moved by hand rather than with `scrollIntoView`, which also scrolls every
 * scrollable ancestor, the shell's `overflow: hidden` frame included, and shifted the app bar.
 * @param scroller The element the navigation scrolls in; nothing happens while it is unmounted.
 * @param entries The entries the navigation is drawn from.
 * @returns Nothing; the watcher lives as long as the calling component.
 */
export const useRevealCurrentRow = (
  scroller: Ref<HTMLElement | null>,
  entries: () => readonly NavigationEntry[],
): void => {
  const route = useRoute()

  /** Watches the scroller's own size; created once it is mounted. */
  let observer: ResizeObserver | null = null

  /**
   * Scrolls the current row to the scroller's nearest edge when it lies outside it.
   * @returns Resolves once the row has been revealed, or found already visible.
   */
  const reveal = async (): Promise<void> => {
    await nextTick()
    const box = scroller.value
    // The exact page first; on a page no row names exactly (a record under a section), the lit
    // section row.
    const current =
      box?.querySelector<HTMLElement>('nav a[aria-current="page"]') ??
      box?.querySelector<HTMLElement>('nav a.router-link-active')
    if (box === null || box === undefined || current === null || current === undefined) {
      return
    }
    const row = current.getBoundingClientRect()
    const view = box.getBoundingClientRect()
    if (row.top < view.top) {
      box.scrollTop -= view.top - row.top
    } else if (row.bottom > view.bottom) {
      box.scrollTop += row.bottom - view.bottom
    }
  }

  watch(
    (): [string, readonly NavigationEntry[]] => {
      return [route.fullPath, entries()]
    },
    reveal,
    { immediate: true, flush: 'post' },
  )

  onMounted((): void => {
    observer = new ResizeObserver((): void => {
      void reveal()
    })
    if (scroller.value !== null) {
      observer.observe(scroller.value)
    }
  })

  onBeforeUnmount((): void => {
    observer?.disconnect()
  })
}
