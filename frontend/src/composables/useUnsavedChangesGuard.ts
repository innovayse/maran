import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'

/** What {@link useUnsavedChangesGuard} hands the screen that owns the confirmation dialog. */
export interface UnsavedChangesGuard {
  /** True while a navigation is held back waiting for the person to choose; the dialog is open then. */
  asking: Ref<boolean>
  /**
   * Answers the held navigation with "stay": the navigation is cancelled and the edits are kept.
   * @returns Nothing.
   */
  stay: () => void
  /**
   * Answers the held navigation with "leave": the navigation proceeds and the edits are lost.
   * @returns Nothing.
   */
  leave: () => void
}

/**
 * Stops a screen from losing unsaved work by accident, on both ways of leaving it.
 *
 * Inside the panel a route change is held back while there is something to lose; the returned
 * `asking` flag opens the screen's confirmation dialog and `stay` / `leave` carry the answer back to
 * the held navigation. Leaving the panel altogether (closing the tab, reloading, following an
 * external link) cannot be given a styled dialog, so the browser's own prompt is requested, which
 * is its only text and is not ours to translate.
 *
 * Only a route change is guarded. A live language switch remounts the routed page without leaving
 * it (rules/vue.md), and the store that holds the draft keeps it through that.
 * @param isDirty Whether there is anything unsaved; read at the moment of leaving, not captured.
 * @param onLeft Called when the screen is really left, whether or not anything was unsaved, so the
 * caller can drop what it held.
 * @returns The dialog flag and its two answers.
 */
export const useUnsavedChangesGuard = (isDirty: Readonly<Ref<boolean>>, onLeft: () => void): UnsavedChangesGuard => {
  const asking: Ref<boolean> = ref(false)

  /** Settles the navigation that is held back; `null` while none is. */
  let settle: ((proceed: boolean) => void) | null = null

  /**
   * Closes the dialog and gives the held navigation its answer.
   * @param proceed Whether the navigation may go on.
   * @returns Nothing.
   */
  const answer = (proceed: boolean): void => {
    asking.value = false
    settle?.(proceed)
    settle = null
  }

  /**
   * Requests the browser's own prompt when the tab is about to close with edits unsaved.
   * @param event The native event.
   * @returns Nothing.
   */
  const onBeforeUnload = (event: BeforeUnloadEvent): void => {
    if (isDirty.value) {
      event.preventDefault()
    }
  }

  /**
   * Keeps the person on the screen.
   * @returns Nothing.
   */
  const stay = (): void => {
    answer(false)
  }

  /**
   * Lets the person go, losing the edits.
   * @returns Nothing.
   */
  const leave = (): void => {
    answer(true)
  }

  /**
   * Holds a navigation back while there are edits to lose.
   * @returns True to let it go, or a promise the dialog settles.
   */
  const guardNavigation = (): boolean | Promise<boolean> => {
    if (!isDirty.value) {
      onLeft()
      return true
    }

    asking.value = true
    return new Promise<boolean>((resolve) => {
      settle = (proceed: boolean): void => {
        if (proceed) {
          onLeft()
        }
        resolve(proceed)
      }
    })
  }

  // A route UPDATE (same screen, another address) loses the draft just as a leave does.
  onBeforeRouteLeave(guardNavigation)
  onBeforeRouteUpdate(guardNavigation)

  onMounted((): void => {
    window.addEventListener('beforeunload', onBeforeUnload)
  })

  onBeforeUnmount((): void => {
    window.removeEventListener('beforeunload', onBeforeUnload)
  })

  return { asking, stay, leave }
}
