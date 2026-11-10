import type { Ref } from 'vue'

/** What {@link useDropdownItems} hands the dropdown. */
export interface DropdownItems {
  /** The panel's enabled items in DOM order. */
  items: () => HTMLElement[]
  /** Moves real focus to the item at a position, clamped to the ends of the list. */
  focusItemAt: (index: number) => void
  /** The position the panel lands focus on when it opens forwards. */
  landingIndex: () => number
  /** Moves focus by one item, stopping at the ends. */
  moveFocus: (step: number) => void
}

/**
 * The keyboard-navigation half of a menu: finding its items and moving focus between them.
 *
 * Items are read from the DOM rather than from a registry, because they arrive through a slot: the
 * dropdown cannot know which of them a caller rendered with `v-if` at any moment.
 * @param menu The open panel, which is queried for its items.
 * @returns The queries and focus moves the dropdown's key handlers use.
 */
export const useDropdownItems = (menu: Ref<HTMLElement | null>): DropdownItems => {
  /**
   * Collects the panel's enabled items in DOM order.
   * @returns The focusable menu items, in the order the user meets them.
   */
  const items = (): HTMLElement[] => {
    return Array.from(
      menu.value?.querySelectorAll<HTMLElement>(
        '[role="menuitem"]:not([disabled]),[role="menuitemradio"]:not([disabled])',
      ) ?? [],
    )
  }

  /**
   * Moves real focus to the item at a position, clamped to the ends of the list.
   * @param index Position to focus; values outside the list clamp to its ends.
   * @returns Nothing; focus moves synchronously.
   */
  const focusItemAt = (index: number): void => {
    const menuItems = items()
    if (menuItems.length === 0) {
      return
    }
    const clamped = Math.min(Math.max(index, 0), menuItems.length - 1)
    // `preventScroll` because the browser scrolls an off-screen focus target into
    // view, and the dropdown re-places its panel on every scroll: focusing the first
    // item could scroll an ancestor and disturb the menu in the same frame it opened.
    menuItems[clamped]?.focus({ preventScroll: true })
  }

  /**
   * Position the panel lands focus on when it opens forwards — the chosen item in
   * a menu that picks a value, the first item in a menu of commands.
   *
   * The distinction is not cosmetic. A menu of `menuitemradio` items that opens
   * on its first item hands the keyboard's most natural gesture — open, then
   * confirm — the power to change a setting the user never asked to change: from
   * a Russian interface, trigger then Enter then Enter selected `English`,
   * because English is first in the language menu and the second Enter (a
   * deliberate press, or one held key's repeat) activated whatever was focused.
   * That was measured, not theorised. Opening on the chosen item makes the same
   * gesture re-confirm the value already in force, which is a no-op.
   *
   * Which item is chosen is read from the items' own `aria-checked` in the DOM,
   * not from a prop naming it and not from a roving `tabindex`:
   *
   * - The items arrive through a slot, so there is no list to name an index into
   *   — the same reason {@link items} queries the DOM rather than keeping a
   *   registry. A prop would also duplicate a fact the caller has already stated
   *   on the item that owns it, and two statements of one fact drift: the tick,
   *   the announcement and the landing place would be free to disagree.
   * - `aria-checked` is not an approximation of the answer, it IS the answer —
   *   it is what a screen reader announces as the current value, so a menu that
   *   lands somewhere else is a menu that contradicts itself out loud.
   * - A roving `tabindex` tracks the item last FOCUSED, not the item selected.
   *   Those differ the moment a user arrows through a menu and dismisses it with
   *   Escape, and landing on what the user last looked at rather than on what is
   *   in force is a different defect wearing the same clothes.
   *
   * Two cases fall back to the first item, and both are deliberate. When nothing
   * is chosen there is no value to preserve and no accidental change to make, so
   * the command-menu answer is right. When the chosen item is DISABLED it is
   * absent from {@link items} and cannot take focus at all, so landing is forced
   * elsewhere; the first enabled item is the least surprising place, and the
   * gesture that follows can no longer re-select the disabled value — it selects
   * a visible neighbour, which the user can see they are on.
   * @returns Index into {@link items} to focus, always within the list when it
   * has any members.
   */
  const landingIndex = (): number => {
    // `items()` already excludes disabled items, so a chosen-but-disabled item
    // reports -1 here and falls back with everything else that has no choice.
    const chosen = items().findIndex((item: HTMLElement): boolean => {
      return item.getAttribute('aria-checked') === 'true'
    })
    return chosen === -1 ? 0 : chosen
  }

  /**
   * Moves focus by one item within the open panel, stopping at the ends rather
   * than wrapping.
   * @param step Direction: 1 forwards, -1 backwards.
   * @returns Nothing; focus moves synchronously.
   */
  const moveFocus = (step: number): void => {
    const menuItems = items()
    const current = menuItems.findIndex((item: HTMLElement): boolean => {
      return item === document.activeElement
    })
    focusItemAt(current + step)
  }

  return { items, focusItemAt, landingIndex, moveFocus }
}
