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
 * It also runs when the CONTENT changes height, not only the scroller's box. Something above the
 * current row can grow after the row was revealed (a row above it gaining a line once its data or
 * its font arrives): the scroller keeps its size, so the resize above never fired, and the lit row
 * ended up 20px under the identity card (measured: 7 of 40 loaded runs). That follow-up applies
 * only while the viewer has not scrolled the row away themselves: the row has to have been in view
 * at the last reveal or scroll, so opening a section by hand never drags the list back.
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

  /** Watches the height of what the scroller holds; the sibling of `observer`, with its own callback. */
  let contentObserver: ResizeObserver | null = null

  /** Whether the current row was in view at the last reveal or scroll, i.e. the viewer is not elsewhere. */
  let rowInView = false

  /**
   * Finds the row to keep in view: the exact page first; on a page no row names exactly (a record
   * under a section), the lit section row.
   * @returns The row, or null while the navigation has not drawn it.
   */
  const findCurrent = (): HTMLElement | null => {
    return (
      scroller.value?.querySelector<HTMLElement>('nav a[aria-current="page"]') ??
      scroller.value?.querySelector<HTMLElement>('nav a.router-link-active') ??
      null
    )
  }

  /**
   * Records whether the current row lies inside the scroller right now.
   * @returns Nothing; updates `rowInView`.
   */
  const measureRowInView = (): void => {
    const current = findCurrent()
    if (scroller.value === null || current === null) {
      return
    }
    const row = current.getBoundingClientRect()
    const view = scroller.value.getBoundingClientRect()
    rowInView = row.top >= view.top && row.bottom <= view.bottom
  }

  /**
   * Scrolls the current row to the scroller's nearest edge when it lies outside it.
   * @returns Resolves once the row has been revealed, or found already visible.
   */
  const reveal = async (): Promise<void> => {
    await nextTick()
    const box = scroller.value
    const current = findCurrent()
    if (box === null || box === undefined || current === null) {
      return
    }
    const row = current.getBoundingClientRect()
    const view = box.getBoundingClientRect()
    if (row.top < view.top) {
      box.scrollTop -= view.top - row.top
    } else if (row.bottom > view.bottom) {
      box.scrollTop += row.bottom - view.bottom
    }
    rowInView = true
  }

  /**
   * Points the content observer at what the scroller holds now: its children and the navigation
   * inside them. The navigation is replaced when the catalogue arrives, so this runs again then.
   * @returns Nothing; observing the same element twice is harmless.
   */
  const observeContent = (): void => {
    if (contentObserver === null || scroller.value === null) {
      return
    }
    for (const element of [
      ...Array.from(scroller.value.children),
      ...Array.from(scroller.value.querySelectorAll('nav')),
    ]) {
      contentObserver.observe(element)
    }
  }

  /**
   * Re-reveals after the content changed height, unless the viewer had scrolled the row away.
   * @returns Resolves once the row has been revealed again, or left alone.
   */
  const followContent = async (): Promise<void> => {
    if (rowInView) {
      await reveal()
    }
  }

  watch(
    (): [string, readonly NavigationEntry[]] => {
      return [route.fullPath, entries()]
    },
    async (): Promise<void> => {
      await nextTick()
      observeContent()
      await reveal()
    },
    { immediate: true, flush: 'post' },
  )

  onMounted((): void => {
    observer = new ResizeObserver((): void => {
      void reveal()
    })
    if (scroller.value !== null) {
      observer.observe(scroller.value)
    }
    // Content growth is watched separately: the scroller's box does not change when it happens.
    contentObserver = new ResizeObserver((): void => {
      void followContent()
    })
    observeContent()
    scroller.value?.addEventListener('scroll', measureRowInView, { passive: true })
  })

  onBeforeUnmount((): void => {
    scroller.value?.removeEventListener('scroll', measureRowInView)
    contentObserver?.disconnect()
    observer?.disconnect()
  })
}
