import { onBeforeUnmount, onMounted, ref, type Ref, type ShallowRef } from 'vue'

/** What {@link useHorizontalOverflow} reports about a scroll container. */
export interface HorizontalOverflow {
  /** Whether the content is wider than the container right now. */
  overflows: Ref<boolean>
  /** Whether content is hidden past the left edge (the user has scrolled). */
  hiddenStart: Ref<boolean>
  /** Whether content is hidden past the right edge (more to scroll to). */
  hiddenEnd: Ref<boolean>
  /** Re-reads the hidden edges; bind it to the container's `scroll` event. */
  onScroll: () => void
}

/**
 * Tracks whether a horizontal scroll container overflows, and which of its edges hide content, for
 * a container that must say so (`UiTable`'s keyboard stop and edge fades).
 *
 * It re-measures on any size change of the container AND of its content, through one
 * `ResizeObserver`, because content can start or stop overflowing when its rows, the language or
 * the viewport change, not only when the container is resized. The observer is disconnected when
 * the calling component unmounts.
 * @param container The scroll container.
 * @param content The element inside it whose growth must be noticed.
 * @returns The overflow state and the scroll handler that keeps it current.
 */
export const useHorizontalOverflow = (
  container: Readonly<ShallowRef<HTMLElement | null>>,
  content: Readonly<ShallowRef<HTMLElement | null>>,
): HorizontalOverflow => {
  const overflows: Ref<boolean> = ref(false)
  const hiddenStart: Ref<boolean> = ref(false)
  const hiddenEnd: Ref<boolean> = ref(false)

  /** Observer that re-measures on any size change of the container or the content. */
  let observer: ResizeObserver | null = null

  /**
   * Re-reads which edges hide content, so a caller's fades follow the scroll position. Uses the
   * absolute offset so a right-to-left layout, where `scrollLeft` runs negative, reads the same.
   * @returns Nothing; updates `hiddenStart` and `hiddenEnd`.
   */
  const onScroll = (): void => {
    const element = container.value
    if (element === null || !overflows.value) {
      hiddenStart.value = false
      hiddenEnd.value = false
      return
    }
    const offset = Math.abs(element.scrollLeft)
    hiddenStart.value = offset > 1
    hiddenEnd.value = offset + element.clientWidth < element.scrollWidth - 1
  }

  /**
   * Re-measures the container. A one-pixel tolerance keeps sub-pixel rounding from turning content
   * that exactly fits into a phantom overflow.
   * @returns Nothing; updates `overflows`.
   */
  const measure = (): void => {
    const element = container.value
    overflows.value = element !== null && element.scrollWidth > element.clientWidth + 1
    onScroll()
  }

  onMounted(() => {
    measure()
    observer = new ResizeObserver(measure)
    if (container.value !== null) {
      observer.observe(container.value)
    }
    if (content.value !== null) {
      observer.observe(content.value)
    }
  })

  onBeforeUnmount(() => {
    observer?.disconnect()
    observer = null
  })

  return { overflows, hiddenStart, hiddenEnd, onScroll }
}
