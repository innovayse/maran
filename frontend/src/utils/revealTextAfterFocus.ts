import { focusableElements } from './focusableElements'

/**
 * Scrolls a scroll container so that the text following its focused control stays in view, up to
 * the next control: the hint or note under a field belongs to that field, and a reader typing into
 * the field must be able to read it.
 *
 * The browser scrolls a focused control into view and stops at the control's own edge, so in a
 * dialog whose body scrolls between a fixed header and a fixed footer, a hint placed under the last
 * field was left beneath the footer while the reader typed (the account deletion dialog, issue
 * #217). Revealing everything up to the next focusable element (or to the end of the content, when
 * the control is the last one) is the general form of that: nothing that can only be read, never
 * focused, is left out of view behind the footer at the moment it is needed. The control itself is
 * never scrolled out of view to make room: when the text is taller than the viewport, the control's
 * top edge wins.
 * @param container The scroll container (the dialog body).
 * @param control The element that just received focus inside it.
 * @returns Nothing; adjusts `container.scrollTop` when more of the following text fits.
 */
export const revealTextAfterFocus = (container: HTMLElement, control: HTMLElement): void => {
  const box = container.getBoundingClientRect()
  /**
   * Where an element starts inside the container's scrolled content.
   * @param element An element inside the container.
   * @returns Its top edge, in pixels from the top of the content.
   */
  const offsetOf = (element: HTMLElement): number => {
    return element.getBoundingClientRect().top - box.top + container.scrollTop
  }

  const controls = focusableElements(container)
  const next = controls[controls.indexOf(control) + 1]
  const textEnd = next === undefined ? container.scrollHeight : offsetOf(next)
  const wantedBottom = Math.min(textEnd, offsetOf(control) + container.clientHeight)
  const visibleBottom = container.scrollTop + container.clientHeight

  if (wantedBottom > visibleBottom) {
    container.scrollTop = wantedBottom - container.clientHeight
  }
}
