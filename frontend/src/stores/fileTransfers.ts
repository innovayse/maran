import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import { ApiError } from '../composables/useApi'
import { baseName, childPath, parentPath } from '../utils/filePath'
import { saveDownload } from '../utils/saveDownload'
import type { ArchiveFormat, FilesOptions, UploadItem } from '../types/file'

/**
 * How many uploads run at once. Browsers allow about six connections to one origin and the
 * panel's own requests need some of them, so a short queue of three keeps the page responsive
 * while a long list of files still finishes in reasonable time. A UI concern, not a domain value.
 */
const MAX_PARALLEL_UPLOADS = 3

/** The backend's code for an upload refused because a file of that name is already there. */
const ENTRY_EXISTS_CODE = 'FilesEntryExists'

/** The archive format a folder download asks for when the server offers it. */
const PREFERRED_ARCHIVE_FORMAT: ArchiveFormat = 'zip'

/**
 * Owns the browser side of moving files: the upload queue (a few at a time, each cancellable and
 * retryable) and downloads, a folder download being an archive written next to the folder and then
 * downloaded. Components read state here and call its actions; they never touch the API layer
 * (rules/vue.md).
 *
 * **No byte-level progress.** The upload goes through `fetch`, which reports none, so an item is
 * `queued`, `uploading` or finished, and `sentBytes` jumps from 0 to the file's size when it ends.
 *
 * **Cancelling** aborts the request; the server counts the bytes it reads, stops, and the agent
 * keeps no partial file, so a cancelled upload leaves nothing behind.
 *
 * Error text is never generated here: a refusal's already-localized sentence is stored verbatim.
 */
export const useFileTransfersStore = defineStore('fileTransfers', () => {
  const api = useFilesApi()

  /** Every file in the queue, in the order it was added, finished ones included until dismissed. */
  const items: Ref<UploadItem[]> = ref([])

  /** The account each queued item uploads into, by item id. Kept apart from the item: it is queue plumbing. */
  const accountOf: Ref<Record<string, string>> = ref({})

  /** Ids of items refused because a file of that name exists, which is the only failure "replace" fixes. */
  const existingConflictIds: Ref<string[]> = ref([])

  /** The limits the server enforces, for the queue's hint; `null` until the first upload reads them. */
  const options: Ref<FilesOptions | null> = ref(null)

  /** The path being downloaded right now (a file, or a folder while it is archived), or `null`. */
  const downloadingPath: Ref<string | null> = ref(null)

  /** The server's sentence for the most recent refused download, or `null`. */
  const downloadErrorMessage: Ref<string | null> = ref(null)

  /**
   * Bumped each time an upload or an archive lands in a folder, so the listing the person is looking
   * at knows to read itself again. A counter, not a flag: two completions in a row are two events.
   */
  const completedCount: Ref<number> = ref(0)

  /** The abort controller of each upload in flight, by item id. Not reactive: it is plumbing, not display. */
  const aborts: Map<string, AbortController> = new Map()

  /** Monotonic source of queue-local item ids. */
  let nextItemNumber = 0

  /** Items still waiting for a free slot, oldest first. */
  const waiting: ComputedRef<UploadItem[]> = computed(() => {
    return items.value.filter((item) => {return item.state === 'queued'})
  })

  /** Items being sent now. */
  const running: ComputedRef<UploadItem[]> = computed(() => {
    return items.value.filter((item) => {return item.state === 'uploading'})
  })

  /** Whether any upload is waiting or running, which is when leaving the page loses work. */
  const busy: ComputedRef<boolean> = computed(() => {
    return waiting.value.length > 0 || running.value.length > 0
  })

  /**
   * Turns a failure into the sentence to show: the server's own text, or `null` for "no answer at
   * all", which the queue replaces with its own words.
   * @param error What the request threw.
   * @returns The server's message, or `null`.
   */
  const reasonOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Whether a failure is the person's own abort, which is an outcome and not an error.
   * @param error What the request threw.
   * @returns True for an aborted request.
   */
  const isAbort = (error: unknown): boolean => {
    return error instanceof DOMException && error.name === 'AbortError'
  }

  /**
   * Whether an upload was refused because the target file exists, which "replace" can resolve.
   * @param id The queue item.
   * @returns True when replacing would let the upload through.
   */
  const refusedForExisting = (id: string): boolean => {
    return existingConflictIds.value.includes(id)
  }

  /**
   * Reads the server's limits once, so the queue can state them. A failure is ignored: the hint is
   * advice, and the upload itself reports whatever is wrong.
   * @returns Resolves once the read has settled.
   */
  const ensureOptions = async (): Promise<void> => {
    if (options.value !== null) {
      return
    }
    try {
      options.value = await api.options()
    } catch {
      // Advice only: the queue works without the hint and the upload carries the real answer.
    }
  }

  /**
   * Finds a queued item by id.
   * @param id The queue item.
   * @returns The item, or `undefined` when it was dismissed.
   */
  const find = (id: string): UploadItem | undefined => {
    return items.value.find((item) => {return item.id === id})
  }

  /**
   * Sends one item and records how it ended. Never throws: every outcome becomes the item's state.
   * @param item The queued item to send.
   * @param overwrite Whether an existing file of that name is replaced.
   * @returns Resolves once the item has finished, failed or been cancelled.
   */
  const send = async (item: UploadItem, overwrite: boolean): Promise<void> => {
    const abort = new AbortController()
    aborts.set(item.id, abort)
    item.state = 'uploading'
    item.error = null
    item.sentBytes = 0

    try {
      await api.upload(
        accountOf.value[item.id],
        childPath(item.directory, item.file.name),
        overwrite,
        item.file,
        abort.signal,
      )
      item.sentBytes = item.file.size
      item.state = 'done'
      completedCount.value += 1
    } catch (error) {
      if (isAbort(error)) {
        item.state = 'cancelled'
      } else {
        item.state = 'failed'
        item.error = reasonOf(error)
        if (error instanceof ApiError && error.code === ENTRY_EXISTS_CODE) {
          existingConflictIds.value = [...existingConflictIds.value, item.id]
        }
      }
    } finally {
      aborts.delete(item.id)
    }
  }

  /**
   * Starts as many queued items as there are free slots. Called whenever a slot may have opened.
   * @returns Nothing; the items run on their own.
   */
  const pump = (): void => {
    const free = MAX_PARALLEL_UPLOADS - running.value.length
    for (const item of waiting.value.slice(0, Math.max(free, 0))) {
      // Marked before the first await inside send(), so the next pass cannot start it twice.
      void send(item, false).then(pump)
    }
  }

  /**
   * Adds files to the queue and starts uploading them. Several chosen at once become several
   * items; whatever the person picked is sent as-is, the server being the judge of size and name.
   * @param accountId The account the files belong to.
   * @param directory The folder they upload into, relative to the home.
   * @param files The files chosen or dropped.
   * @returns Resolves once the files are queued; the uploads continue on their own.
   */
  const enqueue = async (accountId: string, directory: string, files: readonly File[]): Promise<void> => {
    for (const file of files) {
      nextItemNumber += 1
      const id = `upload-${nextItemNumber}`
      accountOf.value = { ...accountOf.value, [id]: accountId }
      items.value.push({ id, file, directory, state: 'queued', sentBytes: 0, error: null })
    }
    pump()
    await ensureOptions()
  }

  /**
   * Cancels a waiting or running upload. A running one is aborted, which the server turns into a
   * stopped write that leaves no partial file.
   * @param id The queue item.
   * @returns Nothing.
   */
  const cancel = (id: string): void => {
    const item = find(id)
    if (item === undefined) {
      return
    }
    if (item.state === 'queued') {
      item.state = 'cancelled'
      return
    }
    // The abort rejects the request, and send() records the cancelled state when it does.
    aborts.get(id)?.abort()
  }

  /**
   * Puts a failed or cancelled item back in the queue.
   * @param id The queue item.
   * @param overwrite Whether an existing file of that name is replaced this time.
   * @returns Nothing; the upload runs on its own.
   */
  const retry = (id: string, overwrite: boolean = false): void => {
    const item = find(id)
    if (item === undefined || (item.state !== 'failed' && item.state !== 'cancelled')) {
      return
    }
    existingConflictIds.value = existingConflictIds.value.filter((entry) => {return entry !== id})
    if (overwrite) {
      // A replacing retry skips the wait: the person asked for exactly this one, now.
      void send(item, true).then(pump)
      return
    }
    item.state = 'queued'
    item.error = null
    pump()
  }

  /**
   * Removes one finished, failed or cancelled item from the list.
   * @param id The queue item.
   * @returns Nothing.
   */
  const dismiss = (id: string): void => {
    const item = find(id)
    if (item === undefined || item.state === 'queued' || item.state === 'uploading') {
      return
    }
    items.value = items.value.filter((entry) => {return entry.id !== id})
    existingConflictIds.value = existingConflictIds.value.filter((entry) => {return entry !== id})
    accountOf.value = Object.fromEntries(Object.entries(accountOf.value).filter(([key]) => {return key !== id}))
  }

  /**
   * Removes every finished, failed or cancelled item, keeping what is still waiting or running.
   * @returns Nothing.
   */
  const dismissFinished = (): void => {
    // `dismiss` replaces the list rather than editing it, so walking the list as it was is safe.
    for (const item of items.value) {
      dismiss(item.id)
    }
  }

  /**
   * Downloads one file through the browser's save flow.
   * @param accountId The account that owns the file.
   * @param path The file, relative to the home.
   * @returns True when the file was handed to the browser; otherwise the reason is in
   * {@link downloadErrorMessage} (`null` there means no answer arrived).
   */
  const download = async (accountId: string, path: string): Promise<boolean> => {
    downloadingPath.value = path
    downloadErrorMessage.value = null
    try {
      saveDownload(await api.download(accountId, path))
      return true
    } catch (error) {
      downloadErrorMessage.value = reasonOf(error)
      return false
    } finally {
      downloadingPath.value = null
    }
  }

  /**
   * Downloads a folder: packs it into an archive written beside it, then downloads that archive.
   * The archive stays in the parent folder afterwards, as the person asked for a file to keep.
   * @param accountId The account that owns the folder.
   * @param folderPath The folder, relative to the home.
   * @returns True when the archive was written and handed to the browser.
   */
  const downloadFolder = async (accountId: string, folderPath: string): Promise<boolean> => {
    downloadingPath.value = folderPath
    downloadErrorMessage.value = null
    try {
      await ensureOptions()
      const formats = options.value?.archiveFormats ?? []
      const format = formats.includes(PREFERRED_ARCHIVE_FORMAT) ? PREFERRED_ARCHIVE_FORMAT : formats[0]
      // The server owns the set of formats; with no answer there is nothing honest to ask for.
      if (format === undefined) {
        downloadErrorMessage.value = null
        return false
      }
      // The format's wire name (`zip`, `tar.gz`) is also the file extension it is written with.
      const archivePath = childPath(parentPath(folderPath), `${baseName(folderPath)}.${format}`)
      await api.archive({ accountId, sourcePaths: [folderPath], destinationPath: archivePath, format })
      completedCount.value += 1
      saveDownload(await api.download(accountId, archivePath))
      return true
    } catch (error) {
      downloadErrorMessage.value = reasonOf(error)
      return false
    } finally {
      downloadingPath.value = null
    }
  }

  /**
   * Forgets a refused download's message once the person has seen it.
   * @returns Nothing.
   */
  const dismissDownloadError = (): void => {
    downloadErrorMessage.value = null
  }

  return {
    items,
    options,
    downloadingPath,
    downloadErrorMessage,
    completedCount,
    waiting,
    running,
    busy,
    refusedForExisting,
    enqueue,
    cancel,
    retry,
    dismiss,
    dismissFinished,
    download,
    downloadFolder,
    dismissDownloadError,
  }
})
