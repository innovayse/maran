import { computed, onScopeDispose, ref, type ComputedRef, type Ref } from 'vue'
import { isSearchTermTooLong } from '../utils/searchTermLimit'

/** A search box whose text is sent once typing pauses. */
export interface DebouncedSearch {
  /** What is typed so far. */
  query: Ref<string>
  /** Whether the typed text is longer than the list accepts; held back from sending when asked to. */
  tooLong: ComputedRef<boolean>
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
 * word typed quickly is one request, not one per letter. With `refuseLongTerm`, a term the server
 * would refuse for its length is not sent at all and `tooLong` says so, for the field's error; a
 * caller whose endpoint takes longer text leaves it off. A pending send is dropped when the scope
 * that owns it is disposed.
 * @param send Receives the text once typing pauses.
 * @param delayMs How long typing must pause, in milliseconds.
 * @param refuseLongTerm Whether a term over the list limit is held back instead of sent.
 * @returns The text and the actions that change it.
 */
export const useDebouncedSearch = (
  send: (text: string) => void,
  delayMs: number,
  refuseLongTerm = false,
): DebouncedSearch => {
  const query: Ref<string> = ref('')

  /** Whether the typed text is longer than the server accepts. */
  const tooLong: ComputedRef<boolean> = computed(() => {
    return isSearchTermTooLong(query.value)
  })

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
    if (refuseLongTerm && tooLong.value) {
      // The server would refuse this term; the field shows why and the list keeps its last answer.
      return
    }
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

  return { query, tooLong, type, reset, cancel }
}
