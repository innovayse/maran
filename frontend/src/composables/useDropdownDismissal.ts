import { onBeforeUnmount, onMounted, type Ref } from 'vue'

/** What {@link useDropdownDismissal} needs to know about the dropdown it watches. */
export interface DropdownDismissalOptions {
  /** Whether the panel is open; an outside press only matters while it is. */
  isOpen: Ref<boolean>
  /** The component's outermost element, which holds the trigger. */
  root: Ref<HTMLElement | null>
  /** The open panel, which is teleported out of the root. */
  menu: Ref<HTMLElement | null>
  /** Closes the panel without moving focus. */
  dismiss: () => void
}

/** What {@link useDropdownDismissal} hands the dropdown. */
export interface DropdownDismissal {
  /** Whether a node belongs to the dropdown — its trigger or its panel. */
  containsNode: (node: Node) => boolean
  /** The `focusout` handler: dismisses the panel when focus leaves the component entirely. */
  onFocusOut: (event: FocusEvent) => void
}

/**
 * The ways a menu is dismissed by something other than a choice or a key: a press outside it, and
 * focus leaving it for somewhere that is not part of it.
 * @param options The elements that make up the dropdown and the dismissal to call.
 * @returns The membership test and the `focusout` handler.
 */
export const useDropdownDismissal = (options: DropdownDismissalOptions): DropdownDismissal => {
  /**
   * Whether a node belongs to this dropdown — its trigger or its panel.
   *
   * The panel is teleported into `body`, so it is NOT a descendant of the component's root element.
   * Asking the root alone, as this used to, would report every click and every focus inside the
   * open menu as "outside" and dismiss the menu the moment it was used.
   * @param node The node to test.
   * @returns True when the node is inside the trigger's box or the panel.
   */
  const containsNode = (node: Node): boolean => {
    return options.root.value?.contains(node) === true || options.menu.value?.contains(node) === true
  }

  /**
   * Closes the panel when focus leaves the component entirely by a route other than Tab
   * (which the dropdown's own Tab handler deals with) — a pointer press elsewhere, a script moving
   * focus.
   * @param event The native focusout event.
   * @returns Nothing; state updates synchronously.
   */
  const onFocusOut = (event: FocusEvent): void => {
    const next = event.relatedTarget
    if (next instanceof Node && containsNode(next)) {
      return
    }
    options.dismiss()
  }

  /**
   * Closes the panel on a click outside the component. Bound to `mousedown` so the menu is gone
   * before the outside target reacts.
   * @param event The document-level pointer event.
   * @returns Nothing; state updates synchronously.
   */
  const onDocumentMouseDown = (event: MouseEvent): void => {
    if (!options.isOpen.value) {
      return
    }
    const target = event.target
    if (target instanceof Node && containsNode(target)) {
      return
    }
    options.dismiss()
  }

  onMounted((): void => {
    document.addEventListener('mousedown', onDocumentMouseDown)
  })

  onBeforeUnmount((): void => {
    document.removeEventListener('mousedown', onDocumentMouseDown)
  })

  return { containsNode, onFocusOut }
}
