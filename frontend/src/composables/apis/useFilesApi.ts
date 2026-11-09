import { useApi } from '../useApi'
import type { ApiDownload } from '../../types/api'
import type { Paged } from '../../types/list'
import type {
  ChangeFilePermissionsRequest,
  CreateFileArchiveRequest,
  CreateFileEntryRequest,
  DeleteFileEntriesRequest,
  EmptiedTrash,
  ExtractFileArchiveRequest,
  FileArchived,
  FileBatchResult,
  FileEntry,
  FileExtracted,
  FileInfo,
  FileListQuery,
  FileSaved,
  FileSearchQuery,
  FileSearchResult,
  FilesApi,
  FilesOptions,
  FileText,
  FileUploaded,
  RenameFileEntryRequest,
  RestoreTrashEntriesRequest,
  SaveFileTextRequest,
  TransferFileEntriesRequest,
  TrashEntry,
  TrashFileEntriesRequest,
  TrashListQuery,
} from '../../types/file'

/** The base of every file manager endpoint. */
const FILES_PATH = '/api/v1/files'

/**
 * Builds the query string of a request that names an account and a path. The path travels in the
 * query, percent-encoded by `URLSearchParams`, never in the URL path: a file name can hold any
 * character, and the server validates the decoded value.
 * @param accountId The account.
 * @param path The path relative to the home; empty for the home.
 * @param extra Further parameters, appended in the order given.
 * @returns The query string including its leading `?`.
 */
const pathQuery = (accountId: string, path: string, extra: Readonly<Record<string, string>> = {}): string => {
  const params = new URLSearchParams({ accountId, path })
  for (const [name, value] of Object.entries(extra)) {
    params.set(name, value)
  }
  return `?${params.toString()}`
}

/**
 * Builds the page, size, search and sort parameters of a paged request, sending the search and the
 * sort only when set, because the panel binds an empty parameter as a missing value and refuses it.
 * @param params The query to add to.
 * @param list The page, size, search and sort.
 * @returns Nothing; the parameters are added in place.
 */
const addListParams = (params: URLSearchParams, list: FileListQuery['list']): void => {
  params.set('page', String(list.page))
  params.set('pageSize', String(list.pageSize))
  if (list.search !== '') {
    params.set('search', list.search)
  }
  if (list.sort !== '') {
    params.set('sort', list.sort)
  }
}

/**
 * Builds the file manager API on top of the shared low-level client. Every call is a named `const`
 * arrow function with its own JSDoc; nothing but endpoint calls lives here. Another account's id
 * answers 404 on every call, so there is nothing to filter on this side.
 * @returns The {@link FilesApi} bound to the panel's file manager endpoints.
 */
export const useFilesApi = (): FilesApi => {
  const api = useApi()

  /**
   * Reads the limits the server enforces; they are the panel's, the same for every account.
   * @param signal Optional abort signal.
   * @returns The limits.
   */
  const options = (signal?: AbortSignal): Promise<FilesOptions> => {
    return api.get<FilesOptions>(`${FILES_PATH}/options`, signal)
  }

  /**
   * Lists one page of a folder.
   * @param query The account, folder, hidden flag and the page, search and sort.
   * @param signal Optional abort signal.
   * @returns The page.
   */
  const list = (query: FileListQuery, signal?: AbortSignal): Promise<Paged<FileEntry>> => {
    const params = new URLSearchParams({
      accountId: query.accountId,
      path: query.path,
      showHidden: String(query.showHidden),
    })
    addListParams(params, query.list)
    return api.get<Paged<FileEntry>>(`${FILES_PATH}/entries?${params.toString()}`, signal)
  }

  /**
   * Reads one entry in full.
   * @param accountId The account.
   * @param path The entry.
   * @param signal Optional abort signal.
   * @returns The entry with its owner and link target.
   */
  const info = (accountId: string, path: string, signal?: AbortSignal): Promise<FileInfo> => {
    return api.get<FileInfo>(`${FILES_PATH}/entries/info${pathQuery(accountId, path)}`, signal)
  }

  /**
   * Searches names under a folder.
   * @param query The account, folder, term and page.
   * @param signal Optional abort signal.
   * @returns The page of matches and whether the search was cut short.
   */
  const search = (query: FileSearchQuery, signal?: AbortSignal): Promise<FileSearchResult> => {
    const extra = { search: query.term, page: String(query.page), pageSize: String(query.pageSize) }
    return api.get<FileSearchResult>(`${FILES_PATH}/search${pathQuery(query.accountId, query.path, extra)}`, signal)
  }

  /**
   * Reads a file's text for the editor.
   * @param accountId The account.
   * @param path The file.
   * @param signal Optional abort signal.
   * @returns The text, or an `ApiError` with code `FilesNotEditable`.
   */
  const readText = (accountId: string, path: string, signal?: AbortSignal): Promise<FileText> => {
    return api.get<FileText>(`${FILES_PATH}/content${pathQuery(accountId, path)}`, signal)
  }

  /**
   * Saves a file's text.
   * @param request The file, the new text and the instant the editor loaded.
   * @param signal Optional abort signal.
   * @returns The saved size and the new modification instant.
   */
  const saveText = (request: SaveFileTextRequest, signal?: AbortSignal): Promise<FileSaved> => {
    return api.put<FileSaved>(`${FILES_PATH}/content`, request, signal)
  }

  /**
   * Uploads one file as a raw body; the account, path and overwrite flag travel in the query.
   * @param accountId The account.
   * @param path The file's destination path, relative to the home.
   * @param overwrite Whether an existing file is replaced.
   * @param body The file's bytes.
   * @param signal Optional abort signal.
   * @returns The stored size.
   */
  const upload = (
    accountId: string,
    path: string,
    overwrite: boolean,
    body: Blob,
    signal?: AbortSignal,
  ): Promise<FileUploaded> => {
    return api.upload<FileUploaded>(
      `${FILES_PATH}/upload${pathQuery(accountId, path, { overwrite: String(overwrite) })}`,
      body,
      signal,
    )
  }

  /**
   * Downloads one file whole, with the name the panel proposed.
   * @param accountId The account.
   * @param path The file.
   * @param signal Optional abort signal.
   * @returns The file and its proposed name.
   */
  const download = (accountId: string, path: string, signal?: AbortSignal): Promise<ApiDownload> => {
    return api.download(`${FILES_PATH}/download${pathQuery(accountId, path)}`, signal)
  }

  /**
   * Creates an empty file or a folder.
   * @param request The account, path and kind.
   * @param signal Optional abort signal.
   * @returns The new entry.
   */
  const create = (request: CreateFileEntryRequest, signal?: AbortSignal): Promise<FileEntry> => {
    return api.post<FileEntry>(`${FILES_PATH}/entries`, request, signal)
  }

  /**
   * Renames an entry within its folder.
   * @param request The account, entry and new name.
   * @param signal Optional abort signal.
   * @returns The renamed entry.
   */
  const rename = (request: RenameFileEntryRequest, signal?: AbortSignal): Promise<FileEntry> => {
    return api.post<FileEntry>(`${FILES_PATH}/rename`, request, signal)
  }

  /**
   * Moves entries to another folder.
   * @param request The account, entries and destination.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  const move = (request: TransferFileEntriesRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/move`, request, signal)
  }

  /**
   * Copies entries to another folder.
   * @param request The account, entries and destination.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  const copy = (request: TransferFileEntriesRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/copy`, request, signal)
  }

  /**
   * Changes an entry's permission bits.
   * @param request The account, entry, mode and recursion.
   * @param signal Optional abort signal.
   * @returns The outcome for the entry.
   */
  const changePermissions = (request: ChangeFilePermissionsRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/permissions`, request, signal)
  }

  /**
   * Moves entries to the trash.
   * @param request The account and entries.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  const trash = (request: TrashFileEntriesRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/trash`, request, signal)
  }

  /**
   * Lists one page of the trash.
   * @param query The account and the page, search and sort.
   * @param signal Optional abort signal.
   * @returns The page of trashed entries.
   */
  const listTrash = (query: TrashListQuery, signal?: AbortSignal): Promise<Paged<TrashEntry>> => {
    const params = new URLSearchParams({ accountId: query.accountId })
    addListParams(params, query.list)
    return api.get<Paged<TrashEntry>>(`${FILES_PATH}/trash?${params.toString()}`, signal)
  }

  /**
   * Restores trashed entries to where they were.
   * @param request The account and trash ids.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  const restore = (request: RestoreTrashEntriesRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/trash/restore`, request, signal)
  }

  /**
   * Empties the trash for good.
   * @param accountId The account.
   * @param signal Optional abort signal.
   * @returns How many entries were removed.
   */
  const emptyTrash = (accountId: string, signal?: AbortSignal): Promise<EmptiedTrash> => {
    return api.delete<EmptiedTrash>(`${FILES_PATH}/trash?${new URLSearchParams({ accountId }).toString()}`, signal)
  }

  /**
   * Deletes entries permanently, bypassing the trash.
   * @param request The account, entries and the explicit confirmation.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  const remove = (request: DeleteFileEntriesRequest, signal?: AbortSignal): Promise<FileBatchResult> => {
    return api.post<FileBatchResult>(`${FILES_PATH}/delete`, request, signal)
  }

  /**
   * Packs entries into an archive.
   * @param request The account, entries, destination and format.
   * @param signal Optional abort signal.
   * @returns The archive's size.
   */
  const archive = (request: CreateFileArchiveRequest, signal?: AbortSignal): Promise<FileArchived> => {
    return api.post<FileArchived>(`${FILES_PATH}/archive`, request, signal)
  }

  /**
   * Extracts an archive into a folder.
   * @param request The account, archive and destination.
   * @param signal Optional abort signal.
   * @returns How many entries were written.
   */
  const extract = (request: ExtractFileArchiveRequest, signal?: AbortSignal): Promise<FileExtracted> => {
    return api.post<FileExtracted>(`${FILES_PATH}/extract`, request, signal)
  }

  return {
    options,
    list,
    info,
    search,
    readText,
    saveText,
    upload,
    download,
    create,
    rename,
    move,
    copy,
    changePermissions,
    trash,
    listTrash,
    restore,
    emptyTrash,
    remove,
    archive,
    extract,
  }
}
