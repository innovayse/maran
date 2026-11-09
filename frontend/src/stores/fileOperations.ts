import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import { ApiError } from '../composables/useApi'
import type { ArchiveFormat, FileBatchResult, FileEntry, FileInfo } from '../types/file'
import type { Read, Submission } from '../types/loading'

/** How many entries the destination picker asks the panel for at a time; one of the sizes it accepts. */
const FOLDER_PAGE_SIZE = 25

/**
 * Owns every mutating action of the file manager (create, rename, move, copy, permissions, trash,
 * permanent delete, archive, extract) and the folder listing the destination picker browses.
 *
 * It never refreshes the browser's listing itself: the page that mounted the dialogs asks its own
 * list to read again once an action reports success, so this store imports no other store
 * (rules/vue.md State).
 *
 * Error text is never generated here: a refusal's already-localized message is kept verbatim in
 * {@link submission}, and an unreachable panel is a `failed` state with no message for the screen
 * to word itself.
 */
export const useFileOperationsStore = defineStore('fileOperations', () => {
  const api = useFilesApi()

  /** Where the latest action stands; `failed` carries the panel's reason. */
  const submission: Ref<Submission> = ref({ state: 'idle' })

  /** The folders of the directory the destination picker is showing, as the panel listed them. */
  const folders: Ref<Read<FileEntry[]>> = ref({ state: 'idle' })

  /** Whether the panel holds further entries of the browsed directory than the ones read so far. */
  const foldersHasMore: Ref<boolean> = ref(false)

  /** Whether a further page of folders is being read, while the ones already read stay on screen. */
  const foldersLoadingMore: Ref<boolean> = ref(false)

  /** The last page of the browsed directory that was read; 0 before the first. */
  let foldersPage = 0

  /** Which listing is current, so the answer of one the person has since left is dropped. */
  let foldersGeneration = 0

  /**
   * Runs one action and records how it ended.
   * @param call The request to make.
   * @returns What the request answered, or `null` when it failed (the reason is in {@link submission}).
   */
  const attempt = async <T>(call: () => Promise<T>): Promise<T | null> => {
    submission.value = { state: 'sending' }
    try {
      const answer = await call()
      submission.value = { state: 'done' }
      return answer
    } catch (error) {
      submission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return null
    }
  }

  /**
   * Forgets the outcome of the last action, as when a dialog opens or closes.
   * @returns Nothing.
   */
  const reset = (): void => {
    submission.value = { state: 'idle' }
    foldersGeneration += 1
    folders.value = { state: 'idle' }
    foldersHasMore.value = false
    foldersLoadingMore.value = false
    foldersPage = 0
  }

  /**
   * Creates an empty file or a folder.
   * @param accountId The account.
   * @param path The new entry's path, relative to the home.
   * @param kind Whether to create a file or a folder.
   * @returns True when the panel created it.
   */
  const create = async (accountId: string, path: string, kind: 'file' | 'directory'): Promise<boolean> => {
    return (await attempt(() => {return api.create({ accountId, path, kind })})) !== null
  }

  /**
   * Renames an entry within its folder.
   * @param accountId The account.
   * @param path The entry.
   * @param newName The new name, one segment.
   * @returns True when the panel renamed it.
   */
  const rename = async (accountId: string, path: string, newName: string): Promise<boolean> => {
    return (await attempt(() => {return api.rename({ accountId, path, newName })})) !== null
  }

  /**
   * Moves entries to another folder.
   * @param accountId The account.
   * @param sourcePaths The entries.
   * @param destinationDirectory The folder they land in.
   * @returns One outcome per entry, or `null` when the request failed.
   */
  const move = (accountId: string, sourcePaths: string[], destinationDirectory: string): Promise<FileBatchResult | null> => {
    return attempt(() => {return api.move({ accountId, sourcePaths, destinationDirectory })})
  }

  /**
   * Copies entries to another folder.
   * @param accountId The account.
   * @param sourcePaths The entries.
   * @param destinationDirectory The folder the copies land in.
   * @returns One outcome per entry, or `null` when the request failed.
   */
  const copy = (accountId: string, sourcePaths: string[], destinationDirectory: string): Promise<FileBatchResult | null> => {
    return attempt(() => {return api.copy({ accountId, sourcePaths, destinationDirectory })})
  }

  /**
   * Changes an entry's permission bits.
   * @param accountId The account.
   * @param path The entry.
   * @param mode The new mode as octal digits.
   * @param recursive Whether the change reaches everything inside a folder.
   * @param directoryMode The folders' mode in a recursive change, or `null` to leave it to the panel.
   * @returns The outcome, or `null` when the request failed.
   */
  const changePermissions = (
    accountId: string,
    path: string,
    mode: string,
    recursive: boolean,
    directoryMode: string | null,
  ): Promise<FileBatchResult | null> => {
    return attempt(() => {
      return api.changePermissions({
        accountId,
        path,
        mode,
        recursive,
        ...(directoryMode === null ? {} : { directoryMode }),
      })
    })
  }

  /**
   * Moves entries to the trash, from which they can be restored.
   * @param accountId The account.
   * @param paths The entries.
   * @returns One outcome per entry, or `null` when the request failed.
   */
  const trash = (accountId: string, paths: string[]): Promise<FileBatchResult | null> => {
    return attempt(() => {return api.trash({ accountId, paths })})
  }

  /**
   * Deletes entries for good. Only a dialog that made the person confirm calls this, which is why
   * the confirmation flag is set here and nowhere else.
   * @param accountId The account.
   * @param paths The entries.
   * @returns One outcome per entry, or `null` when the request failed.
   */
  const remove = (accountId: string, paths: string[]): Promise<FileBatchResult | null> => {
    return attempt(() => {return api.remove({ accountId, paths, confirm: true })})
  }

  /**
   * Packs entries into an archive.
   * @param accountId The account.
   * @param sourcePaths The entries.
   * @param destinationPath The archive file written.
   * @param format The archive format.
   * @returns True when the panel wrote the archive.
   */
  const archive = async (
    accountId: string,
    sourcePaths: string[],
    destinationPath: string,
    format: ArchiveFormat,
  ): Promise<boolean> => {
    return (await attempt(() => {return api.archive({ accountId, sourcePaths, destinationPath, format })})) !== null
  }

  /**
   * Extracts an archive into a folder.
   * @param accountId The account.
   * @param archivePath The archive.
   * @param destinationDirectory The folder it is extracted into.
   * @returns True when the panel extracted it.
   */
  const extract = async (accountId: string, archivePath: string, destinationDirectory: string): Promise<boolean> => {
    return (await attempt(() => {return api.extract({ accountId, archivePath, destinationDirectory })})) !== null
  }

  /**
   * Reads one entry in full for the permissions dialog's read-only owner line. A failure is
   * swallowed: the owner line is a courtesy and must not block the change.
   * @param accountId The account.
   * @param path The entry.
   * @returns The entry, or `null` when it could not be read.
   */
  const info = async (accountId: string, path: string): Promise<FileInfo | null> => {
    try {
      return await api.info(accountId, path)
    } catch {
      return null
    }
  }

  /**
   * Reads one page of a directory for the destination picker and works out whether more follows.
   * @param accountId The account.
   * @param path The directory browsed, relative to the home.
   * @param search The name filter, applied by the panel; empty for none.
   * @param page The 1-based page to read.
   * @returns The folders of that page, how many entries the pages up to it hold and whether further pages are worth reading.
   */
  const readFolderPage = async (
    accountId: string,
    path: string,
    search: string,
    page: number,
  ): Promise<{ folders: FileEntry[]; read: number; hasMore: boolean }> => {
    const answer = await api.list({
      accountId,
      path,
      showHidden: false,
      list: { page, pageSize: FOLDER_PAGE_SIZE, search, sort: 'name' },
    })
    const read = (page - 1) * FOLDER_PAGE_SIZE + answer.items.length
    // The panel lists a directory's folders before its files, so an unsearched page holding a file
    // is the last one that can hold a folder. A search is ordered by the panel's own rules, so it
    // is read to its end.
    const reachedFiles = search === '' && answer.items.some((entry) => {return entry.kind !== 'directory'})
    return {
      folders: answer.items.filter((entry) => {return entry.kind === 'directory'}),
      read,
      hasMore: !reachedFiles && read < answer.total,
    }
  }

  /**
   * Lists the first page of one directory's folders for the destination picker. The listing is the
   * panel's, so which names exist, which are hidden and which match the search is its decision; only
   * entries it calls directories are kept.
   * @param accountId The account.
   * @param path The directory browsed, relative to the home.
   * @param search The name filter, applied by the panel; empty for none.
   * @returns Resolves once the read has settled.
   */
  const loadFolders = async (accountId: string, path: string, search: string): Promise<void> => {
    foldersGeneration += 1
    const generation = foldersGeneration
    folders.value = { state: 'loading' }
    foldersHasMore.value = false
    foldersLoadingMore.value = false
    try {
      const first = await readFolderPage(accountId, path, search, 1)
      if (generation !== foldersGeneration) {
        return
      }
      foldersPage = 1
      folders.value = { state: 'loaded', value: first.folders }
      foldersHasMore.value = first.hasMore
    } catch (error) {
      if (generation === foldersGeneration) {
        folders.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Reads the next page of the directory last listed and appends its folders to the ones shown.
   * @param accountId The account.
   * @param path The directory browsed, the one {@link loadFolders} was last called with.
   * @param search The name filter, the one {@link loadFolders} was last called with.
   * @returns Resolves once the read has settled.
   */
  const loadMoreFolders = async (accountId: string, path: string, search: string): Promise<void> => {
    const shown = folders.value
    if (shown.state !== 'loaded' || !foldersHasMore.value || foldersLoadingMore.value) {
      return
    }
    const generation = foldersGeneration
    foldersLoadingMore.value = true
    try {
      const next = await readFolderPage(accountId, path, search, foldersPage + 1)
      if (generation !== foldersGeneration) {
        return
      }
      foldersPage += 1
      folders.value = { state: 'loaded', value: [...shown.value, ...next.folders] }
      foldersHasMore.value = next.hasMore
    } catch (error) {
      if (generation === foldersGeneration) {
        folders.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
        foldersHasMore.value = false
      }
    } finally {
      if (generation === foldersGeneration) {
        foldersLoadingMore.value = false
      }
    }
  }

  return {
    submission,
    folders,
    foldersHasMore,
    foldersLoadingMore,
    reset,
    create,
    rename,
    move,
    copy,
    changePermissions,
    trash,
    remove,
    archive,
    extract,
    info,
    loadFolders,
    loadMoreFolders,
  }
})
