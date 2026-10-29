import { nextTick, onBeforeUnmount, watch, type Ref } from 'vue'
import { focusableElements } from '../utils/focusableElements'

/** Where focus may be sent when a dialog closes: an element, or a getter evaluated at close time. */
export type FocusTarget = HTMLElement | (() => HTMLElement | null | undefined) | null | undefined

/** What {@link useFocusTrap} needs from the dialog that owns the panel. */
export interface FocusTrapOptions {
  /** The dialog panel (`tabindex="-1"`); its focusable descendants are what Tab cycles through. */
  panel: Ref<HTMLElement | null>
  /** Reactive source of "is the dialog shown"; the owner's prop, read through a getter. */
  open: () => boolean
  /**
   * Element to focus when the dialog opens. Defaults to the first focusable descendant, then to
   * the panel itself, so focus is never left behind on the page the dialog covers.
   */
  initialFocus?: () => HTMLElement | null | undefined
  /**
   * Where focus goes on close when the element that had it at open time cannot take it back (see
   * {@link isFocusable}). When this is absent or unusable too, the `<main>` landmark is used.
   */
  returnFocusTo?: () => FocusTarget
}

/** What {@link useFocusTrap} hands back to the dialog's template. */
export interface FocusTrap {
  /**
   * Keydown handler for Tab and Shift+Tab, bound on the dialog's root (`@keydown.tab`). Wraps focus
   * at both ends so the keyboard cannot walk off a modal into the page it covers.
   */
  onTab: (event: KeyboardEvent) => void
}

/**
 * Whether an element can really take focus right now.
 *
 * "Is it still in the document" is not enough for the element a dialog remembered at open time: the
 * credential dialogs open from a form's submit button, which is `disabled` while the request is in
 * flight, so what the browser reported as focused was `<body>`. A disabled control, a node removed
 * from the document, a hidden element and `<body>` itself all silently refuse `focus()` or park it
 * at the top of the document — the failure the fallback exists for.
 * @param element The remembered element.
 * @returns True when focusing it will land focus on it.
 */
const isFocusable = (element: HTMLElement | null | undefined): element is HTMLElement => {
  if (element === null || element === undefined) {
    return false
  }
  return (
    element !== document.body &&
    element.isConnected &&
    !element.matches(':disabled') &&
    !element.closest('[inert]') &&
    element.checkVisibility()
  )
}

/**
 * Resolves a {@link FocusTarget} to an element.
 * @param target An element, a getter, or nothing.
 * @returns The element, or null when there is none.
 */
const resolveTarget = (target: FocusTarget): HTMLElement | null => {
  const resolved = typeof target === 'function' ? target() : target
  return resolved ?? null
}

/**
 * Modal focus management shared by every overlay that takes over the keyboard (the kit's
 * `UiModal`, the shell's navigation drawer): focus moves in on open, Tab wraps inside the panel,
 * and focus goes back on close. One implementation, because the three halves must agree — a trap
 * that wraps differently from the way focus entered, or a restore that fails where the capture
 * succeeded, strands a keyboard user — and two copies had already begun to drift.
 *
 * **Return target.** The element focused when the dialog opened is remembered and re-focused on
 * close, when it can still take focus ({@link isFocusable}). Otherwise the fallback is the
 * caller's `returnFocusTo`, then the `<main>` landmark (`tabindex="-1"`, the skip link's target):
 * a place inside the content, never `<body>`, which would restart the keyboard user at the top of
 * the document. Restoring waits one tick so a control that is re-enabled by the same state change
 * that closed the dialog is enabled when it is asked, and is skipped when something else has taken
 * focus in the meantime (one dialog closing as the next opens must not steal it back).
 *
 * `immediate` matters for a dialog mounted already open (`v-if` beside `:open`): without it the
 * watcher never fires and focus stays on the page behind. A dialog mounted closed takes the close
 * arm on that first pass, where nothing has been captured yet, so it moves focus nowhere.
 * @param options The panel, the open state and the optional focus targets.
 * @returns The Tab handler to bind on the dialog.
 */
export const useFocusTrap = (options: FocusTrapOptions): FocusTrap => {
  /** Whether focus has been captured for the current showing, and so is owed back. */
  let captured = false
  /** The element that had focus when the dialog opened; null when nothing sensible did. */
  let opener: HTMLElement | null = null
  /**
   * The panel element of the current showing, kept here because the owner's template ref is nulled
   * in the very flush that closes the dialog, before the restore below runs — while a leaving
   * panel (a transition) may still be in the document holding focus.
   */
  let shownPanel: HTMLElement | null = null

  /**
   * Moves focus to the best available return target (see {@link useFocusTrap}).
   * @returns Nothing; focus moves synchronously.
   */
  const restoreFocus = (): void => {
    const candidates: (HTMLElement | null)[] = [
      opener,
      resolveTarget(options.returnFocusTo?.()),
      document.querySelector<HTMLElement>('main'),
    ]
    candidates.find(isFocusable)?.focus()
    captured = false
    opener = null
    shownPanel = null
  }

  /**
   * Whether nobody else has claimed focus since the dialog closed: it is still inside the (leaving)
   * panel, or has fallen to `<body>` because that panel was removed.
   * @returns True when restoring focus would not take it from another component.
   */
  const focusIsUnclaimed = (): boolean => {
    const active = document.activeElement
    return active === null || active === document.body || shownPanel?.contains(active) === true
  }

  watch(
    options.open,
    async (isOpen: boolean): Promise<void> => {
      if (isOpen) {
        captured = true
        opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
        // On the immediate pass this resolves after the mounting render flush, so the panel
        // exists to focus into by the time it returns.
        await nextTick()
        shownPanel = options.panel.value
        const elements = focusableElements(options.panel.value)
        // Fall back to the panel itself when there is no focusable content yet.
        ;(options.initialFocus?.() ?? elements[0] ?? options.panel.value)?.focus()
        return
      }
      if (!captured) {
        return
      }
      await nextTick()
      if (focusIsUnclaimed()) {
        restoreFocus()
      }
      captured = false
      shownPanel = null
    },
    { immediate: true },
  )

  // A route change can tear the dialog down while it is still open, and the watcher never runs for
  // that. Without this the keyboard user would be left on a detached node.
  onBeforeUnmount((): void => {
    if (options.open() && captured) {
      restoreFocus()
    }
  })

  /**
   * Wraps Tab and Shift+Tab at the panel's ends, and pulls focus back in when it has escaped.
   * @param event The keyboard event for Tab or Shift+Tab.
   * @returns Nothing; focus moves synchronously.
   */
  const onTab = (event: KeyboardEvent): void => {
    const panel = options.panel.value
    const elements = focusableElements(panel)
    const first = elements[0]
    const last = elements[elements.length - 1]
    if (first === undefined || last === undefined) {
      // Nothing focusable inside: keep focus on the panel rather than letting Tab leave the
      // dialog for the inert page behind it.
      event.preventDefault()
      return
    }
    const active = document.activeElement
    // A backdrop click leaves focus on <body>, outside the panel; without this the next Tab would
    // walk into the page behind the dialog instead of into it.
    if (!(active instanceof Node) || panel?.contains(active) !== true) {
      event.preventDefault()
      first.focus()
      return
    }
    if (event.shiftKey && (active === first || active === panel)) {
      event.preventDefault()
      last.focus()
      return
    }
    if (!event.shiftKey && active === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return { onTab }
}
