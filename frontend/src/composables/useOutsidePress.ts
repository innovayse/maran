import { onScopeDispose, watch, type Ref } from 'vue'

/**
 * Calls `onOutside` when the pointer is pressed outside `root` while `active` is true — how a popup
 * closes on a click elsewhere. The document listener exists only while `active` is true, and is
 * removed when the owning scope is disposed.
 * @param root The element a press must land outside of.
 * @param active Whether the popup is open.
 * @param onOutside What to do on a press outside.
 * @returns Nothing.
 */
export const useOutsidePress = (root: Ref<HTMLElement | null>, active: Ref<boolean>, onOutside: () => void): void => {
  /**
   * Reports a document-level press that landed outside `root`.
   * @param event The pointer event.
   * @returns Nothing.
   */
  const onDocumentMouseDown = (event: MouseEvent): void => {
    const target = event.target
    if (active.value && !(target instanceof Node && root.value?.contains(target) === true)) {
      onOutside()
    }
  }

  watch(active, (on: boolean): void => {
    if (on) {
      document.addEventListener('mousedown', onDocumentMouseDown)
      return
    }
    document.removeEventListener('mousedown', onDocumentMouseDown)
  })

  onScopeDispose((): void => {
    document.removeEventListener('mousedown', onDocumentMouseDown)
  })
}
