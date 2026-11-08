import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useFileOperationsStore } from '../stores/fileOperations'
import { childPath } from '../utils/filePath'
import type { ArchiveFormat, FileBatchAction, FileBatchResult, FileEntry, FileEntryAction } from '../types/file'

/** Everything the file operations can carry out: the menu's and the bar's actions plus the toolbar's two creates. */
export type FileOperationAction = FileEntryAction | FileBatchAction | 'createFile' | 'createFolder'

/** An operation the person asked for, waiting to be carried out. */
export interface FileOperationOrder {
  /** What was asked. `edit` and `download` are not an operation's business and are ignored. */
  action: FileOperationAction
  /** The entries it is for, as they were listed; empty for the two creates. */
  entries: FileEntry[]
}

/** What {@link useFileOperationRunner} reads from its host; the host's props satisfy it. */
export interface FileOperationSource {
  /** The order being asked about, or `null` while none is. */
  readonly order: FileOperationOrder | null
  /** The account whose files are acted on. */
  readonly accountId: string
  /** The folder being browsed, relative to the home: where new entries and archives land. */
  readonly folder: string
}

/** What {@link useFileOperationRunner} hands the dialog host. */
export interface FileOperationRunner {
  /** The batch outcome to show when some items failed, or `null`. */
  result: Ref<FileBatchResult | null>
  /** The action of the active order. */
  action: ComputedRef<FileOperationAction | null>
  /** The entries of the active order. */
  entries: ComputedRef<FileEntry[]>
  /** The first entry, for the single-entry dialogs. */
  first: ComputedRef<FileEntry | null>
  /** Whether a request is in flight. */
  submitting: ComputedRef<boolean>
  /** The panel's reason the last request failed, worded for the person; `null` when it has not failed. */
  errorMessage: ComputedRef<string | null>
  /** The owner line of the permissions dialog, or `null` until the entry has been read. */
  ownerText: Ref<string | null>
  /** Starts an order clean: no old failure and whatever the action needs read first. */
  begin: (order: FileOperationOrder | null) => void
  /** Closes the operation and forgets its outcome. */
  close: () => void
  /** Creates the entry named in the dialog inside the folder being browsed. */
  create: (name: string) => Promise<void>
  /** Renames the first entry. */
  rename: (newName: string) => Promise<void>
  /** Moves or copies the entries into the chosen folder. */
  transfer: (destination: string) => Promise<void>
  /** Changes the first entry's permission bits. */
  changePermissions: (mode: string, recursive: boolean, directoryMode: string | null) => Promise<void>
  /** Packs the entries into an archive written in the folder being browsed. */
  archive: (fileName: string, format: ArchiveFormat) => Promise<void>
  /** Extracts the first entry into the chosen folder. */
  extract: (destination: string) => Promise<void>
  /** Moves the entries to the trash. */
  trash: () => Promise<void>
  /** Deletes the entries for good; only the confirmation dialog calls this. */
  remove: () => Promise<void>
}

/**
 * Carries out the operation the file browser asked for through the operations store and reports how
 * it went. **Outcomes.** A single-entry action that succeeds closes its dialog and reports `done`. A
 * batch runs every item in order and reports per item; when every item worked the dialog closes,
 * otherwise a notice lists what failed with the server's own reasons, and `done` is reported anyway
 * because part of the folder changed. A request that fails outright keeps its dialog open with the
 * panel's message above the form.
 *
 * Nothing here decides what is allowed: the server and the agent are the authority.
 * @param source What it reads from its host; read lazily, so a changing prop is followed.
 * @param onDone Called when the operation changed something, so the listing should be read again.
 * @param onClose Called when the operation is over, whatever its outcome.
 * @returns The state the dialogs read and the actions they call.
 */
export const useFileOperationRunner = (
  source: FileOperationSource,
  onDone: () => void,
  onClose: () => void,
): FileOperationRunner => {
  const { t } = useI18n()
  const store = useFileOperationsStore()

  /** The batch outcome to show when some items failed, or `null`. */
  const result: Ref<FileBatchResult | null> = ref(null)

  /** The owner line of the permissions dialog, or `null` until the entry has been read. */
  const ownerText: Ref<string | null> = ref(null)

  /** The order currently being asked about; hidden while a batch notice is on screen. */
  const active: ComputedRef<FileOperationOrder | null> = computed(() => {
    return result.value === null ? source.order : null
  })

  /** The action of the active order. */
  const action: ComputedRef<FileOperationAction | null> = computed(() => {
    return active.value?.action ?? null
  })

  /** The entries of the active order. */
  const entries: ComputedRef<FileEntry[]> = computed(() => {
    return active.value?.entries ?? []
  })

  /** The first entry, for the single-entry dialogs. */
  const first: ComputedRef<FileEntry | null> = computed(() => {
    return entries.value[0] ?? null
  })

  /** Whether a request is in flight. */
  const submitting: ComputedRef<boolean> = computed(() => {
    return store.submission.state === 'sending'
  })

  /** The panel's reason the last request failed, worded for the person; `null` when it has not failed. */
  const errorMessage: ComputedRef<string | null> = computed(() => {
    const submission = store.submission
    return submission.state === 'failed' ? (submission.message ?? t('common.list.unreachable')) : null
  })

  /** The paths of the active order's entries. */
  const paths: ComputedRef<string[]> = computed(() => {
    return entries.value.map((entry) => {return entry.path})
  })

  /**
   * Starts an order clean: no old failure, no old outcome, and the owner read when it is needed.
   * @param order The order that begins, or `null` when none is asked.
   * @returns Nothing.
   */
  const begin = (order: FileOperationOrder | null): void => {
    store.reset()
    result.value = null
    ownerText.value = null
    if (order?.action === 'permissions' && order.entries[0] !== undefined) {
      void store.info(source.accountId, order.entries[0].path).then((info) => {
        ownerText.value = info === null ? null : `${info.owner}:${info.group}`
      })
    }
  }

  /**
   * Closes the operation and forgets its outcome.
   * @returns Nothing.
   */
  const close = (): void => {
    result.value = null
    onClose()
  }

  /**
   * Reports a finished single-entry action: the listing changed and the dialog is done.
   * @returns Nothing.
   */
  const finish = (): void => {
    onDone()
    close()
  }

  /**
   * Handles a batch outcome: all done closes, a partial one shows the notice.
   * @param outcome What the store returned, or `null` when the request failed outright.
   * @returns Nothing.
   */
  const finishBatch = (outcome: FileBatchResult | null): void => {
    if (outcome === null) {
      return
    }
    onDone()
    if (outcome.items.every((item) => {return item.succeeded})) {
      close()
      return
    }
    result.value = outcome
  }

  /**
   * Creates the entry named in the dialog inside the folder being browsed.
   * @param name The typed name.
   * @returns Resolves once the request settled.
   */
  const create = async (name: string): Promise<void> => {
    const kind = action.value === 'createFolder' ? 'directory' : 'file'
    if (await store.create(source.accountId, childPath(source.folder, name), kind)) {
      finish()
    }
  }

  /**
   * Renames the first entry.
   * @param newName The typed name.
   * @returns Resolves once the request settled.
   */
  const rename = async (newName: string): Promise<void> => {
    if (first.value !== null && (await store.rename(source.accountId, first.value.path, newName))) {
      finish()
    }
  }

  /**
   * Moves or copies the entries into the chosen folder.
   * @param destination The chosen folder.
   * @returns Resolves once the request settled.
   */
  const transfer = async (destination: string): Promise<void> => {
    const run = action.value === 'move' ? store.move : store.copy
    finishBatch(await run(source.accountId, paths.value, destination))
  }

  /**
   * Changes the first entry's permission bits.
   * @param mode The three octal digits.
   * @param recursive Whether the change reaches everything inside a folder.
   * @param directoryMode The folders' mode in a recursive change, or `null`.
   * @returns Resolves once the request settled.
   */
  const changePermissions = async (
    mode: string,
    recursive: boolean,
    directoryMode: string | null,
  ): Promise<void> => {
    if (first.value !== null) {
      finishBatch(
        await store.changePermissions(source.accountId, first.value.path, mode, recursive, directoryMode),
      )
    }
  }

  /**
   * Packs the entries into an archive written in the folder being browsed.
   * @param fileName The archive's file name.
   * @param format The archive format.
   * @returns Resolves once the request settled.
   */
  const archive = async (fileName: string, format: ArchiveFormat): Promise<void> => {
    const destination = childPath(source.folder, fileName)
    if (await store.archive(source.accountId, paths.value, destination, format)) {
      finish()
    }
  }

  /**
   * Extracts the first entry into the chosen folder.
   * @param destination The chosen folder.
   * @returns Resolves once the request settled.
   */
  const extract = async (destination: string): Promise<void> => {
    if (first.value !== null && (await store.extract(source.accountId, first.value.path, destination))) {
      finish()
    }
  }

  /**
   * Moves the entries to the trash.
   * @returns Resolves once the request settled.
   */
  const trash = async (): Promise<void> => {
    finishBatch(await store.trash(source.accountId, paths.value))
  }

  /**
   * Deletes the entries for good; only the confirmation dialog calls this.
   * @returns Resolves once the request settled.
   */
  const remove = async (): Promise<void> => {
    finishBatch(await store.remove(source.accountId, paths.value))
  }

  return {
    result,
    action,
    entries,
    first,
    submitting,
    errorMessage,
    ownerText,
    begin,
    close,
    create,
    rename,
    transfer,
    changePermissions,
    archive,
    extract,
    trash,
    remove,
  }
}
