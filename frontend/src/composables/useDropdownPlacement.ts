import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'

/** Gap in CSS pixels between the trigger and the panel. */
const TRIGGER_GAP = 4

/**
 * Gap in CSS pixels kept between the panel and the viewport edge when deciding
 * whether it fits. Small on purpose: it only has to stop the menu resting flush
 * against the edge, not to reserve a margin the design does not draw.
 */
const VIEWPORT_MARGIN = 8

/** What {@link useDropdownPlacement} needs to know about the dropdown it places. */
export interface DropdownPlacementOptions {
  /** The trigger button the panel is placed against. */
  trigger: Ref<HTMLButtonElement | null>
  /** The open panel, measured for its size. */
  panel: Ref<HTMLElement | null>
  /** Whether the panel is open; a closed dropdown ignores viewport changes. */
  isOpen: Ref<boolean>
  /** Which edge of the trigger the panel aligns to; read at every placement, so it may change. */
  align: () => 'start' | 'end'
  /** Called when the trigger has been carried off screen and the panel belongs to nothing. */
  onLost: () => void
}

/** What {@link useDropdownPlacement} hands the dropdown. */
export interface DropdownPlacement {
  /** Inline style placing the panel in viewport coordinates, hidden until it has been placed. */
  panelStyle: ComputedRef<Record<string, string>>
  /** Marks the panel as not yet placed, so it is not painted at the previous open's coordinates. */
  hide: () => void
  /** Shows the panel once it has been placed. */
  reveal: () => void
  /** Measures the trigger and the panel and decides where the panel goes. */
  place: () => void
  /** Starts following the trigger on scroll; call when the panel opens. */
  followScroll: () => void
  /** Stops following the trigger on scroll; call when the panel closes. */
  stopFollowingScroll: () => void
}

/**
 * Where the dropdown's panel is drawn, in viewport coordinates, and how it stays with its trigger.
 *
 * The panel is rendered into `body` and positioned in viewport coordinates, NOT next to the trigger
 * in the document. A menu absolutely positioned inside its trigger's box is clipped by any ancestor
 * that scrolls, and the panel's own tables are exactly that: `UiTable` scrolls horizontally inside
 * its own container so a wide table never moves the page sideways. Escaping the ancestor is the only
 * fix that does not give up the table's own scrolling. The cost is that the panel no longer travels
 * with its trigger by itself, so a scroll that moves the trigger re-places the panel against it
 * (see {@link followTrigger}) and a viewport resize re-decides its side.
 *
 * The panel picks its own vertical side: it prefers to open downwards and flips upwards when the
 * space below the trigger cannot hold it. A menu in a sidebar footer is the case that forced this —
 * it sits against the bottom edge of the viewport, so a menu that only ever opened downwards was
 * unreachable there.
 * @param options The trigger, the panel and what to do when the trigger is lost.
 * @returns The panel's style and the calls that drive its placement.
 */
export const useDropdownPlacement = (options: DropdownPlacementOptions): DropdownPlacement => {
  /**
   * Whether the open panel has been measured and placed against its trigger yet.
   *
   * The panel cannot be positioned until it exists, so for one flush after it opens it is still
   * carrying the coordinates of whatever the previous open left behind — `{ top: 0, left: 0 }` on
   * the first one, which draws a fixed-position menu in the viewport's top-left corner. It is kept
   * `visibility: hidden` until {@link place} has run, so that frame is not painted anywhere and the
   * panel appears only where it belongs.
   */
  const isPlaced: Ref<boolean> = ref(false)

  /**
   * The panel's viewport coordinates while it is open.
   *
   * Held as numbers rather than as classes because the panel is positioned against the trigger's
   * measured box, and no set of utility classes can express "wherever that element happens to be
   * right now".
   */
  const panelOffset: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

  /**
   * Where the trigger sat, in viewport coordinates, when the panel was last placed against it. It is
   * what tells a scroll that MOVED the trigger from one that did not.
   *
   * The distinction is not academic. A pointer press focuses the trigger, and a browser scrolls a
   * focused control into view when it is not fully visible — but it dispatches that scroll event
   * asynchronously, a frame later, by which time the dropdown has already opened its panel and
   * started listening. So the scroll that BROUGHT the user to the trigger arrived after the panel
   * existed and dismissed it in the same breath: on a long screen the menu could not be opened at
   * all, and on a short one it opened fine, which is why this survived review. Comparing the
   * trigger's box answers the question the dismissal is actually asking — has the panel been left
   * floating beside a control that has moved? — instead of trusting the arrival of an event whose
   * timing says nothing about that.
   *
   * That comparison is necessary and it was not sufficient, because the deferred scroll can also
   * MOVE the trigger — by a single pixel, when the trigger sits just short of the scroll container's
   * edge — and then the comparison reports movement that the user never caused. What removes that
   * case is not a larger tolerance here but the dropdown's trigger press, which stops the browser
   * scrolling on focus at all; this comparison remains for every other scroll event that arrives
   * without having moved anything.
   */
  const anchor: Ref<{ top: number; left: number }> = ref({ top: 0, left: 0 })

  /** Inline placement of the panel, in viewport coordinates. */
  const panelStyle: ComputedRef<Record<string, string>> = computed(() => {
    return {
      top: `${panelOffset.value.top}px`,
      left: `${panelOffset.value.left}px`,
      visibility: isPlaced.value ? 'visible' : 'hidden',
    }
  })

  /**
   * Decides which side the open panel is drawn on from the room the trigger actually has, and where
   * it goes. Measured rather than assumed: the same component is used in a table row with the whole
   * page below it and in a sidebar footer with nothing below it at all.
   *
   * Downwards stays the preference — it is where a menu is expected — and the panel flips up only
   * when it does not fit below AND fits better above, so a viewport too short for the menu either
   * way keeps the familiar direction.
   *
   * "Fits below" is judged on the panel's real bottom edge — trigger, the {@link TRIGGER_GAP} and
   * the panel's height — against the viewport less its {@link VIEWPORT_MARGIN}, exactly; there is no
   * tolerance that lets a panel hang off the bottom. A menu that does not fit under its trigger is
   * drawn above it, whole, rather than below it, clipped: the side is a consequence of where the
   * trigger happens to sit on the screen, never a promise a caller may rely on.
   * @returns Nothing; the offset updates synchronously.
   */
  const place = (): void => {
    const trigger = options.trigger.value
    const panel = options.panel.value
    if (trigger === null || panel === null) {
      return
    }

    const triggerRect = trigger.getBoundingClientRect()
    const panelRect = panel.getBoundingClientRect()

    // Recorded wherever the panel is placed, so `followTrigger` compares against the
    // box this placement was actually derived from and not an older one.
    anchor.value = { top: triggerRect.top, left: triggerRect.left }

    const roomBelow = window.innerHeight - triggerRect.bottom - VIEWPORT_MARGIN
    const roomAbove = triggerRect.top - VIEWPORT_MARGIN

    const bottomIfBelow = triggerRect.bottom + TRIGGER_GAP + panelRect.height
    const fitsBelow = bottomIfBelow <= window.innerHeight - VIEWPORT_MARGIN

    const above = !fitsBelow && roomAbove > roomBelow

    // `end` aligns the panel's right edge with the trigger's, which is what keeps
    // a menu in the last column of a table from opening off the right of the
    // viewport. Both edges are then clamped, because a trigger near an edge can
    // put a wide panel outside the viewport whichever way it is aligned.
    const preferredLeft = options.align() === 'end' ? triggerRect.right - panelRect.width : triggerRect.left
    const furthestLeft = window.innerWidth - panelRect.width - VIEWPORT_MARGIN
    const left = Math.max(VIEWPORT_MARGIN, Math.min(preferredLeft, furthestLeft))

    const top = above ? triggerRect.top - panelRect.height - TRIGGER_GAP : triggerRect.bottom + TRIGGER_GAP

    panelOffset.value = { top, left }
  }

  /**
   * Keeps an open panel with its trigger when a scroll has MOVED that trigger, and closes the panel
   * once the trigger has been carried off screen.
   *
   * A panel positioned in viewport coordinates does not travel with its trigger, so a scroll leaves
   * the menu floating beside a control that is no longer there. The dropdown used to close on any
   * such move. That answer looked honest and was not, because "the trigger moved" does not mean "the
   * user scrolled": the browser itself scrolls to reveal things, and a menu opened on the last row
   * of a scroll container was measured being dismissed by a ONE PIXEL adjustment nobody asked for,
   * one press in five under load. No tolerance around the comparison can separate those two cases —
   * a pixel of user scroll and a pixel of browser scroll are the same pixel — so the panel follows
   * its anchor instead, which is what a menu bound to a row should do anyway. The cost is one
   * re-placement per scroll frame for the single panel that is open, which is two rect reads.
   *
   * It still closes when the trigger has left the viewport entirely: a menu for a row that is no
   * longer on screen belongs to nothing, and following it would park the panel against an edge.
   *
   * What is measured is the TRIGGER's box, because the question is whether the placement recorded in
   * {@link anchor} still describes where the trigger is. A scroll event that arrives without having
   * moved it costs one rect read and nothing else.
   *
   * Bound in the capture phase so it also sees scrolling inside a container — a scroll event on an
   * element does not bubble to the document.
   * @returns Nothing; the placement or the open state updates synchronously.
   */
  const followTrigger = (): void => {
    if (!options.isOpen.value) {
      return
    }

    const trigger = options.trigger.value
    if (trigger === null) {
      options.onLost()
      return
    }

    const rect = trigger.getBoundingClientRect()
    // Sub-pixel tolerance: a fractional layout position must not read as movement,
    // and re-placing on every frame of a smooth scroll that changed nothing is
    // work for no one.
    if (Math.abs(rect.top - anchor.value.top) <= 0.5 && Math.abs(rect.left - anchor.value.left) <= 0.5) {
      return
    }

    const isOnScreen =
      rect.bottom > 0 && rect.top < window.innerHeight && rect.right > 0 && rect.left < window.innerWidth
    if (isOnScreen) {
      place()
      return
    }
    options.onLost()
  }

  /**
   * Re-decides the panel's side when the viewport changes under an open menu — a rotated phone, an
   * on-screen keyboard, a resized window — so a menu that fit below when it opened does not stay
   * there once it no longer does.
   * @returns Nothing; the placement updates synchronously.
   */
  const onViewportResize = (): void => {
    if (!options.isOpen.value) {
      return
    }
    place()
  }

  /**
   * Hides the panel until it has been measured.
   * @returns Nothing.
   */
  const hide = (): void => {
    isPlaced.value = false
  }

  /**
   * Shows the panel, once {@link place} has run.
   * @returns Nothing.
   */
  const reveal = (): void => {
    isPlaced.value = true
  }

  /**
   * Starts listening for scrolls. Bound when the panel opens rather than on mount: a table of twenty
   * rows is twenty triggers, and twenty listeners on every scroll frame is work done for a panel
   * that is not on screen.
   * @returns Nothing.
   */
  const followScroll = (): void => {
    window.addEventListener('scroll', followTrigger, true)
  }

  /**
   * Stops listening for scrolls.
   * @returns Nothing.
   */
  const stopFollowingScroll = (): void => {
    window.removeEventListener('scroll', followTrigger, true)
  }

  onMounted((): void => {
    window.addEventListener('resize', onViewportResize)
  })

  onBeforeUnmount((): void => {
    window.removeEventListener('resize', onViewportResize)
    // Also here, because a component unmounted while its panel is open — a row
    // removed from the table under an open menu — never reaches the close path.
    stopFollowingScroll()
  })

  return { panelStyle, hide, reveal, place, followScroll, stopFollowingScroll }
}
