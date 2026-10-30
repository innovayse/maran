import { onScopeDispose, ref, type Ref } from 'vue'

/** A search box whose text is sent once typing pauses. */
export interface DebouncedSearch {
  /** What is typed so far. */
  query: Ref<string>
  /**
   * Records new text and schedules sending it; a keystroke before the pause ends restarts the wait.
   * @param text The field's text.
   * @returns Nothing.
   */
  type: (text: string) => void
  /**
   * Empties the text and drops any search not sent yet.
   * @returns Nothing.
   */
  reset: () => void
  /**
   * Drops a search that has not been sent yet, keeping the text.
   * @returns Nothing.
   */
  cancel: () => void
}

/**
 * Paces a server search: the text reaches `send` only after typing has paused for `delayMs`, so a
 * word typed quickly is one request, not one per letter. A pending send is dropped when the scope
 * that owns it is disposed.
 * @param send Receives the text once typing pauses.
 * @param delayMs How long typing must pause, in milliseconds.
 * @returns The text and the actions that change it.
 */
export const useDebouncedSearch = (send: (text: string) => void, delayMs: number): DebouncedSearch => {
  const query: Ref<string> = ref('')

  /** The pending send, or `undefined` when none is waiting. */
  let timer: ReturnType<typeof setTimeout> | undefined

  /**
   * Drops a send that has not happened yet.
   * @returns Nothing.
   */
  const cancel = (): void => {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
  }

  /**
   * Records text and schedules sending it.
   * @param text The field's text.
   * @returns Nothing.
   */
  const type = (text: string): void => {
    query.value = text
    cancel()
    timer = setTimeout((): void => {
      timer = undefined
      send(query.value)
    }, delayMs)
  }

  /**
   * Empties the text and drops any pending send.
   * @returns Nothing.
   */
  const reset = (): void => {
    cancel()
    query.value = ''
  }

  onScopeDispose(cancel)

  return { query, type, reset, cancel }
}
