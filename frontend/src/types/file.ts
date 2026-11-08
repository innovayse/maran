import type { ListRequest, Paged } from './list'
import type { ApiDownload } from './api'

/**
 * The kinds of entry a folder listing shows, mirroring the backend's `FileEntryKind`. A symbolic
 * link is drawn as what it points at (`file` or `directory`) and carries `isSymlink` beside it;
 * `other` is anything the manager can neither open nor edit (a socket, a device node).
 */
export type FileEntryKind = 'file' | 'directory' | 'other'

/**
 * The columns a folder listing can be ordered by, exactly as the backend's sort map names them.
 * A descending order is the same key with a leading `-` (`-modified`), the way every list sends it.
 */
export type FileSortField = 'name' | 'modified' | 'size'

/**
 * One row of a folder listing or of a search, mirroring the backend's `FileEntryDto`.
 *
 * Every path in this file is relative to the account's home and written with `/`; the empty
 * string is the home itself. The panel never builds an absolute path and the browser never sends
 * one: the agent resolves the relative path under the account's home.
 */
export interface FileEntry {
  /** The entry's own name, one path segment. */
  name: string
  /** The entry's path relative to the home, which is what every later request names it by. */
  path: string
  /** Whether the entry is a file, a folder or something else. */
  kind: FileEntryKind
  /** Size in bytes; for a folder the size of the directory node itself, not of its contents. */
  sizeBytes: number
  /** The instant the entry was last modified, as an ISO-8601 string. */
  modifiedAt: string
  /** The permission bits as three octal digits (owner, group, others), e.g. `644`; special bits are not reported. */
  mode: string
  /** Whether the entry is a symbolic link; the link is shown, never followed out of the home. */
  isSymlink: boolean
}

/**
 * What `GET /api/v1/files/entries/info` answers, mirroring the backend's `FileInfoDto`: one entry
 * in full, for the properties dialog.
 */
export interface FileInfo extends FileEntry {
  /** The owning user's numeric id as text (the agent reports ids, not names), shown read-only. */
  owner: string
  /** The owning group's numeric id as text, shown read-only. */
  group: string
}

/**
 * One page of a search, mirroring the backend's search answer: the usual page plus whether the
 * agent stopped at one of its depth or result ceilings, so the screen can say the list is partial.
 */
export interface FileSearchResult extends Paged<FileEntry> {
  /** True when the agent hit a ceiling; the matches shown are then not all of them. */
  truncated: boolean
}

/**
 * What `GET /api/v1/files/content` answers, mirroring the backend's `FileTextDto`: a file small
 * enough and plain enough to edit. A file over the edit limit, with a NUL byte or not valid UTF-8,
 * is refused instead with the code `FilesNotEditable` and offered for download only.
 */
export interface FileText {
  /** The file's text, to be rendered as text and never as markup. */
  content: string
  /** The instant the file was last modified; sent back on save so a stale tab is caught. */
  modifiedAt: string
  /** The file's permission bits, which a save keeps. */
  mode: string
  /** The file's size in bytes. */
  sizeBytes: number
}

/** What `PUT /api/v1/files/content` answered, mirroring the backend's `FileSavedDto`. */
export interface FileSaved {
  /** The size of the saved file in bytes. */
  sizeBytes: number
  /** The file's new modification instant, to send as the next save's expected value. */
  modifiedAt: string
}

/** What `POST /api/v1/files/upload` answered, mirroring the backend's `FileUploadedDto`. */
export interface FileUploaded {
  /** The number of bytes the panel received and the agent stored. */
  sizeBytes: number
}

/**
 * The outcome of one item of a batch, mirroring the backend's `FileBatchItemResultDto`. A batch
 * runs every item in order and a failure of one does not stop the others.
 */
export interface FileBatchItemResult {
  /** The path (or trash id) the item named. */
  subject: string
  /** Whether the operation was done for this item. */
  succeeded: boolean
  /** The machine-stable outcome code, for behaviour only; null on success. */
  code: string | null
  /** The reason in the caller's language, already localized by the server; null on success. */
  message: string | null
}

/** What every batch route answers, mirroring the backend's `FileBatchResultDto`. */
export interface FileBatchResult {
  /** One outcome per requested item, in the order requested. */
  items: FileBatchItemResult[]
}

/**
 * One row of the trash, mirroring the backend's `TrashEntryDto`. A trashed entry is addressed by
 * its trash id, never by a path: the original path may have been reused since.
 */
export interface TrashEntry {
  /** The identity a restore names the entry by. */
  id: string
  /** The entry's name, one path segment. */
  name: string
  /** Where the entry lived before it was trashed, relative to the home. */
  originalPath: string
  /** Whether it was a file or a folder. */
  kind: FileEntryKind
  /** Size in bytes; for a folder the total of what it holds when the agent reports it. */
  sizeBytes: number
  /** The instant it was moved to the trash, as an ISO-8601 string. */
  deletedAt: string
  /** The instant the retention job will remove it for good, as an ISO-8601 string. */
  expiresAt: string
}

/** What `DELETE /api/v1/files/trash` answered, mirroring the backend's `EmptiedTrashDto`. */
export interface EmptiedTrash {
  /** How many entries were removed. */
  count: number
}

/** What `POST /api/v1/files/archive` answered, mirroring the backend's `FileArchivedDto`. */
export interface FileArchived {
  /** The size of the archive written, in bytes. */
  sizeBytes: number
}

/** What `POST /api/v1/files/extract` answered, mirroring the backend's `FileExtractedDto`. */
export interface FileExtracted {
  /** How many entries the archive held and the agent wrote. */
  entryCount: number
}

/** The archive formats the manager can write: a closed set the server names in its options. */
export type ArchiveFormat = 'zip' | 'tar.gz'

/**
 * What `GET /api/v1/files/options` answers, mirroring the backend's `FilesOptionsDto`. The limits
 * live on the server so the SPA holds no constant of its own (rules/vue.md): it shows these and
 * the server enforces them.
 */
export interface FilesOptions {
  /** The largest file an upload may carry, in bytes. */
  maxUploadBytes: number
  /** The largest file the text editor opens, in bytes. */
  maxEditBytes: number
  /** The largest file a download serves, in bytes. */
  maxDownloadBytes: number
  /** The most items one batch request may name. */
  maxBatchItems: number
  /** How many days an entry stays in the trash before it is removed for good. */
  trashRetentionDays: number
  /** The most entries an extraction writes before the agent stops it. */
  extractMaxEntries: number
  /** The most bytes an extraction writes before the agent stops it. */
  extractMaxBytes: number
  /** The highest ratio of extracted to archive size the agent tolerates. */
  extractMaxRatio: number
  /** The formats an archive can be written in. */
  archiveFormats: ArchiveFormat[]
}

/**
 * The state of one file in the browser's upload queue. It exists only in the SPA (the server has
 * no notion of a queue), so it is declared here as the one shape the transfer store and the queue
 * components share.
 */
export type UploadState = 'queued' | 'uploading' | 'done' | 'failed' | 'cancelled'

/** One file in the upload queue. */
export interface UploadItem {
  /** A queue-local identity, so a row survives its neighbours changing state. */
  id: string
  /** The file the person chose or dropped. */
  file: File
  /** The folder it is uploading into, relative to the home. */
  directory: string
  /** Where the item is in its life. */
  state: UploadState
  /**
   * Bytes sent so far. `fetch` reports none, so this is 0 until the request ends and then the
   * file's size: the queue shows a state per file, not a percentage.
   */
  sentBytes: number
  /** The server's reason in the caller's language when {@link state} is `failed`; otherwise null. */
  error: string | null
}

/** Query of `GET /api/v1/files/entries`: which folder of which account, and how it is shown. */
export interface FileListQuery {
  /** The account whose files are listed. */
  accountId: string
  /** The folder listed, relative to the home; empty for the home. */
  path: string
  /** Whether names beginning with a dot are included. */
  showHidden: boolean
  /** The page, size, free-text name filter and sort (a {@link FileSortField}, `-` for descending). */
  list: Pick<ListRequest, 'page' | 'pageSize' | 'search' | 'sort'>
}

/** Body of `PUT /api/v1/files/content`, binding the backend's `SaveFileTextCommand`. */
export interface SaveFileTextRequest {
  /** The account that owns the file. */
  accountId: string
  /** The file saved, relative to the home. */
  path: string
  /** The complete new text. */
  content: string
  /** The modification instant the editor loaded; a newer file on disk answers `FilesEditConflict`. */
  expectedModifiedAt?: string
}

/** Body of `POST /api/v1/files/entries`, binding the backend's `CreateFileEntryCommand`. */
export interface CreateFileEntryRequest {
  /** The account that owns the new entry. */
  accountId: string
  /** The new entry's path, relative to the home. */
  path: string
  /** Whether to create an empty file or a folder. */
  kind: 'file' | 'directory'
}

/** Body of `POST /api/v1/files/rename`, binding the backend's `RenameFileEntryCommand`. */
export interface RenameFileEntryRequest {
  /** The account that owns the entry. */
  accountId: string
  /** The entry renamed, relative to the home. */
  path: string
  /** The new name: one segment, no separators. */
  newName: string
}

/** Body of `POST /api/v1/files/move` and `POST /api/v1/files/copy`, binding the move and copy commands. */
export interface TransferFileEntriesRequest {
  /** The account that owns the entries. */
  accountId: string
  /** The entries moved or copied, relative to the home. */
  sourcePaths: string[]
  /** The folder they land in, relative to the home. */
  destinationDirectory: string
}

/** Body of `POST /api/v1/files/permissions`, binding the backend's `ChangeFilePermissionsCommand`. */
export interface ChangeFilePermissionsRequest {
  /** The account that owns the entry. */
  accountId: string
  /** The entry changed, relative to the home. */
  path: string
  /** The new permission bits as octal digits, 0 to 777; setuid, setgid and sticky are refused. */
  mode: string
  /** Whether the change applies to everything inside a folder too. */
  recursive: boolean
  /**
   * The bits folders get in a recursive change, as octal digits, 1 to 777. Left out, the panel gives
   * folders the files' bits with execute added wherever read is set.
   */
  directoryMode?: string
}

/** Body of `POST /api/v1/files/trash`, binding the backend's `TrashFileEntriesCommand`. */
export interface TrashFileEntriesRequest {
  /** The account that owns the entries. */
  accountId: string
  /** The entries moved to the trash, relative to the home. */
  paths: string[]
}

/** Query of `GET /api/v1/files/trash`: the account and the usual page, search and sort. */
export interface TrashListQuery {
  /** The account whose trash is listed. */
  accountId: string
  /** The page, size, free-text search and sort. */
  list: Pick<ListRequest, 'page' | 'pageSize' | 'search' | 'sort'>
}

/** Body of `POST /api/v1/files/trash/restore`, binding the backend's `RestoreTrashEntriesCommand`. */
export interface RestoreTrashEntriesRequest {
  /** The account that owns the trash. */
  accountId: string
  /** The trash ids restored to where they were. */
  trashIds: string[]
}

/** Body of `POST /api/v1/files/delete`, binding the backend's `DeleteFileEntriesCommand`. */
export interface DeleteFileEntriesRequest {
  /** The account that owns the entries. */
  accountId: string
  /** The entries deleted for good, relative to the home. */
  paths: string[]
  /** Must be true: the server refuses a permanent delete that was not confirmed. */
  confirm: boolean
}

/** Body of `POST /api/v1/files/archive`, binding the backend's `CreateFileArchiveCommand`. */
export interface CreateFileArchiveRequest {
  /** The account that owns the entries. */
  accountId: string
  /** The entries put in the archive, relative to the home. */
  sourcePaths: string[]
  /** The archive file written, relative to the home. */
  destinationPath: string
  /** The archive format. */
  format: ArchiveFormat
}

/** Body of `POST /api/v1/files/extract`, binding the backend's `ExtractFileArchiveCommand`. */
export interface ExtractFileArchiveRequest {
  /** The account that owns the archive. */
  accountId: string
  /** The archive extracted, relative to the home. */
  archivePath: string
  /** The folder it is extracted into, relative to the home. */
  destinationDirectory: string
}

/** Query of `GET /api/v1/files/search`: a name search under one folder. */
export interface FileSearchQuery {
  /** The account whose files are searched. */
  accountId: string
  /** The folder the search starts in, relative to the home. */
  path: string
  /** What to look for in names. */
  term: string
  /** The page asked for. */
  page: number
  /** Rows per page. */
  pageSize: number
}

/**
 * The typed calls to the file manager's endpoints, returned by `useFilesApi`. Every path is
 * relative to the account's home; another account's id answers 404 on every call, never 403.
 */
export interface FilesApi {
  /**
   * Reads the limits the server enforces; they are the panel's, the same for every account.
   * @param signal Optional abort signal.
   * @returns The limits.
   */
  options: (signal?: AbortSignal) => Promise<FilesOptions>

  /**
   * Lists one page of a folder.
   * @param query The account, folder, hidden flag and the page, search and sort.
   * @param signal Optional abort signal.
   * @returns The page, folders first under the default order.
   */
  list: (query: FileListQuery, signal?: AbortSignal) => Promise<Paged<FileEntry>>

  /**
   * Reads one entry in full.
   * @param accountId The account.
   * @param path The entry.
   * @param signal Optional abort signal.
   * @returns The entry with its owner and link target.
   */
  info: (accountId: string, path: string, signal?: AbortSignal) => Promise<FileInfo>

  /**
   * Searches names under a folder.
   * @param query The account, folder, term and page.
   * @param signal Optional abort signal.
   * @returns The page of matches and whether the search was cut short.
   */
  search: (query: FileSearchQuery, signal?: AbortSignal) => Promise<FileSearchResult>

  /**
   * Reads a file's text for the editor.
   * @param accountId The account.
   * @param path The file.
   * @param signal Optional abort signal.
   * @returns The text, or an `ApiError` with code `FilesNotEditable`.
   */
  readText: (accountId: string, path: string, signal?: AbortSignal) => Promise<FileText>

  /**
   * Saves a file's text.
   * @param request The file, the new text and the instant the editor loaded.
   * @param signal Optional abort signal.
   * @returns The saved size and the new modification instant.
   */
  saveText: (request: SaveFileTextRequest, signal?: AbortSignal) => Promise<FileSaved>

  /**
   * Uploads one file as a raw body.
   * @param accountId The account.
   * @param path The file's destination path, relative to the home.
   * @param overwrite Whether an existing file is replaced.
   * @param body The file's bytes.
   * @param signal Optional abort signal; aborting cancels the upload and the agent keeps no partial file.
   * @returns The stored size.
   */
  upload: (accountId: string, path: string, overwrite: boolean, body: Blob, signal?: AbortSignal) => Promise<FileUploaded>

  /**
   * Downloads one file.
   * @param accountId The account.
   * @param path The file.
   * @param signal Optional abort signal.
   * @returns The file and its proposed name.
   */
  download: (accountId: string, path: string, signal?: AbortSignal) => Promise<ApiDownload>

  /**
   * Creates an empty file or a folder.
   * @param request The account, path and kind.
   * @param signal Optional abort signal.
   * @returns The new entry.
   */
  create: (request: CreateFileEntryRequest, signal?: AbortSignal) => Promise<FileEntry>

  /**
   * Renames an entry within its folder.
   * @param request The account, entry and new name.
   * @param signal Optional abort signal.
   * @returns The renamed entry.
   */
  rename: (request: RenameFileEntryRequest, signal?: AbortSignal) => Promise<FileEntry>

  /**
   * Moves entries to another folder.
   * @param request The account, entries and destination.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  move: (request: TransferFileEntriesRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Copies entries to another folder.
   * @param request The account, entries and destination.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  copy: (request: TransferFileEntriesRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Changes an entry's permission bits.
   * @param request The account, entry, mode and recursion.
   * @param signal Optional abort signal.
   * @returns The outcome for the entry.
   */
  changePermissions: (request: ChangeFilePermissionsRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Moves entries to the trash.
   * @param request The account and entries.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  trash: (request: TrashFileEntriesRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Lists one page of the trash.
   * @param query The account and the page, search and sort.
   * @param signal Optional abort signal.
   * @returns The page of trashed entries.
   */
  listTrash: (query: TrashListQuery, signal?: AbortSignal) => Promise<Paged<TrashEntry>>

  /**
   * Restores trashed entries to where they were.
   * @param request The account and trash ids.
   * @param signal Optional abort signal.
   * @returns One outcome per entry; a target that exists again is a per-item refusal.
   */
  restore: (request: RestoreTrashEntriesRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Empties the trash for good.
   * @param accountId The account.
   * @param signal Optional abort signal.
   * @returns How many entries were removed.
   */
  emptyTrash: (accountId: string, signal?: AbortSignal) => Promise<EmptiedTrash>

  /**
   * Deletes entries permanently, bypassing the trash.
   * @param request The account, entries and the explicit confirmation.
   * @param signal Optional abort signal.
   * @returns One outcome per entry.
   */
  remove: (request: DeleteFileEntriesRequest, signal?: AbortSignal) => Promise<FileBatchResult>

  /**
   * Packs entries into an archive.
   * @param request The account, entries, destination and format.
   * @param signal Optional abort signal.
   * @returns The archive's size.
   */
  archive: (request: CreateFileArchiveRequest, signal?: AbortSignal) => Promise<FileArchived>

  /**
   * Extracts an archive into a folder.
   * @param request The account, archive and destination.
   * @param signal Optional abort signal.
   * @returns How many entries were written.
   */
  extract: (request: ExtractFileArchiveRequest, signal?: AbortSignal) => Promise<FileExtracted>
}

/** What a row's menu can ask for. */
export type FileEntryAction =
  | 'edit'
  | 'download'
  | 'rename'
  | 'move'
  | 'copy'
  | 'permissions'
  | 'archive'
  | 'extract'
  | 'trash'
  | 'delete'

/** What the selection bar can ask for: the actions that make sense for several entries at once. */
export type FileBatchAction = 'move' | 'copy' | 'archive' | 'trash' | 'delete'
