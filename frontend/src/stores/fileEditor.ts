import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import { ApiError } from '../composables/useApi'
import type { FilesOptions, FileText } from '../types/file'

/**
 * The backend's code for a file the editor cannot open (over the edit limit, binary, or not valid
 * UTF-8). Used for behaviour only: it turns the failure into the "download instead" state. The
 * sentence the person reads is the server's own.
 */
const NOT_EDITABLE_CODE = 'FilesNotEditable'

/** The backend's code for a save refused because the file changed on disk after it was loaded. */
const EDIT_CONFLICT_CODE = 'FilesEditConflict'

/** Where the editor stands: nothing opened, a read in flight, text in hand, a file it cannot edit, or a failed read. */
export type FileEditorStatus = 'idle' | 'loading' | 'ready' | 'notEditable' | 'failed'

/**
 * Owns the one text file open in the editor: the text as loaded, the text as edited, the instant it
 * was loaded at (sent back on save so a stale tab is caught), and the save and conflict states. The
 * editor page reads state here and calls its actions; it never touches the API layer (rules/vue.md).
 *
 * **Optimistic concurrency.** A save carries the modification instant the editor loaded. When the
 * file on disk is newer the server answers `FilesEditConflict` and nothing is written; the store
 * then holds {@link conflict} until the person reloads the file (and loses their edit) or saves
 * anyway. The server's check is not atomic with its write, so a very narrow window remains.
 *
 * **The draft outlives the page.** The store keeps the edited text when the routed page is
 * remounted, as it is by a live language switch (rules/vue.md), and {@link open} keeps an unsaved
 * draft for the same file instead of reading it again.
 *
 * Error text is never generated here: when the backend refuses, its already-localized `title` or
 * `detail` is stored verbatim.
 */
export const useFileEditorStore = defineStore('fileEditor', () => {
  const api = useFilesApi()

  /** The account whose file is open; empty when none is. */
  const accountId: Ref<string> = ref('')

  /** The open file's path relative to the home; empty when none is. */
  const path: Ref<string> = ref('')

  /** Where the editor stands. */
  const status: Ref<FileEditorStatus> = ref('idle')

  /** The text as last read from, or saved to, the server. */
  const original: Ref<string> = ref('')

  /** The text as the person has edited it; this is what the textarea is bound to. */
  const draft: Ref<string> = ref('')

  /** The modification instant of the loaded text, sent back on save; `null` before a read. */
  const modifiedAt: Ref<string | null> = ref(null)

  /** The permission bits of the open file, shown read-only; a save keeps them. */
  const mode: Ref<string> = ref('')

  /** The limits the server enforces, for the size counter; `null` until they are read. */
  const options: Ref<FilesOptions | null> = ref(null)

  /** True while a save request is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** The server's sentence for the most recent refused read, or `null`. */
  const loadErrorMessage: Ref<string | null> = ref(null)

  /** The server's sentence for the most recent refused save, or `null`. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** Whether the latest save was refused because the file changed on disk since it was loaded. */
  const conflict: Ref<boolean> = ref(false)

  /** Whether the latest save succeeded and nothing has been typed since; drives the "saved" notice. */
  const justSaved: Ref<boolean> = ref(false)

  /** Aborts the read in flight when another file is opened or the editor is closed. */
  let loadAbort: AbortController | null = null

  /** Whether the edited text differs from what the server holds. */
  const dirty: ComputedRef<boolean> = computed(() => {
    return draft.value !== original.value
  })

  /** The edited text's size in bytes as UTF-8, which is what the server counts against its limit. */
  const draftBytes: ComputedRef<number> = computed(() => {
    return new TextEncoder().encode(draft.value).length
  })

  /**
   * Whether the edited text is over the limit the server reported. Advice only: the server
   * refuses an oversized save itself, so before the limits arrive this errs permissive.
   */
  const overLimit: ComputedRef<boolean> = computed(() => {
    return options.value !== null && draftBytes.value > options.value.maxEditBytes
  })

  /** Whether the save button may act: there is a change, it fits, and no save is already running. */
  const canSave: ComputedRef<boolean> = computed(() => {
    return status.value === 'ready' && dirty.value && !overLimit.value && !saving.value
  })

  /**
   * Turns a failure into the sentence to show: the server's own text, or `null` for "no answer at
   * all", which the page replaces with its own words.
   * @param error What the request threw.
   * @returns The server's message, or `null`.
   */
  const reasonOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Takes the text the server answered with as both the baseline and the draft.
   * @param text What the server holds.
   * @returns Nothing.
   */
  const adopt = (text: FileText): void => {
    original.value = text.content
    draft.value = text.content
    modifiedAt.value = text.modifiedAt
    mode.value = text.mode
    justSaved.value = false
  }

  /**
   * Reads the file and the server's limits. A second call for the file already open with unsaved
   * edits does nothing: the draft is the person's work and a remount must not replace it.
   * @param nextAccountId The account that owns the file.
   * @param nextPath The file, relative to the home.
   * @returns Resolves once the read has settled, successfully or not.
   */
  const open = async (nextAccountId: string, nextPath: string): Promise<void> => {
    if (nextAccountId === accountId.value && nextPath === path.value && status.value === 'ready' && dirty.value) {
      return
    }

    loadAbort?.abort()
    const abort = new AbortController()
    loadAbort = abort

    accountId.value = nextAccountId
    path.value = nextPath
    status.value = 'loading'
    loadErrorMessage.value = null
    saveErrorMessage.value = null
    conflict.value = false

    try {
      // The limits and the text are independent reads; asking both at once halves the wait.
      const [limits, text] = await Promise.all([
        api.options(abort.signal),
        api.readText(nextAccountId, nextPath, abort.signal),
      ])
      options.value = limits
      adopt(text)
      status.value = 'ready'
    } catch (error) {
      if (abort.signal.aborted) {
        // A newer open() or close() owns the state now; this read's outcome is moot.
        return
      }
      loadErrorMessage.value = reasonOf(error)
      status.value = error instanceof ApiError && error.code === NOT_EDITABLE_CODE ? 'notEditable' : 'failed'
    }
  }

  /**
   * Saves the draft. The modification instant the editor loaded goes with it unless `force` is set.
   * @param force Whether to save over whatever is on disk now, the answer to a conflict.
   * @returns True when the server stored the text; the caller reads {@link saveErrorMessage} and
   * {@link conflict} for the reason when it did not.
   */
  const save = async (force: boolean = false): Promise<boolean> => {
    if (status.value !== 'ready' || saving.value) {
      return false
    }

    saving.value = true
    saveErrorMessage.value = null
    conflict.value = false
    // Held in a local: the draft can change while the request is in flight, and what the server
    // stored is what was sent, not what is in the box when the answer arrives.
    const sent = draft.value

    try {
      const saved = await api.saveText({
        accountId: accountId.value,
        path: path.value,
        content: sent,
        expectedModifiedAt: force || modifiedAt.value === null ? undefined : modifiedAt.value,
      })
      original.value = sent
      modifiedAt.value = saved.modifiedAt
      justSaved.value = true
      return true
    } catch (error) {
      saveErrorMessage.value = reasonOf(error)
      conflict.value = error instanceof ApiError && error.code === EDIT_CONFLICT_CODE
      return false
    } finally {
      saving.value = false
    }
  }

  /**
   * Discards the edits and reads the file again, the way out of a conflict that keeps the disk's
   * version.
   * @returns Resolves once the read has settled.
   */
  const reload = async (): Promise<void> => {
    const keptAccountId = accountId.value
    const keptPath = path.value
    // Cleared first so open() does not mistake the old draft for work worth protecting.
    draft.value = original.value
    await open(keptAccountId, keptPath)
  }

  /**
   * Takes the person's edit and retires the "saved" notice.
   * @param text The textarea's new text.
   * @returns Nothing.
   */
  const edit = (text: string): void => {
    draft.value = text
    justSaved.value = false
  }

  /**
   * Forgets the open file entirely, for when the person leaves the editor.
   * @returns Nothing.
   */
  const close = (): void => {
    loadAbort?.abort()
    loadAbort = null
    accountId.value = ''
    path.value = ''
    status.value = 'idle'
    original.value = ''
    draft.value = ''
    modifiedAt.value = null
    mode.value = ''
    loadErrorMessage.value = null
    saveErrorMessage.value = null
    conflict.value = false
    justSaved.value = false
  }

  return {
    accountId,
    path,
    status,
    original,
    draft,
    modifiedAt,
    mode,
    options,
    saving,
    loadErrorMessage,
    saveErrorMessage,
    conflict,
    justSaved,
    dirty,
    draftBytes,
    overLimit,
    canSave,
    open,
    save,
    reload,
    edit,
    close,
  }
})
