import type { Page, Route } from '@playwright/test'
import { Buffer } from 'node:buffer'
import type { FilesOptions } from '../../src/types/file'

/** The text file the stub holds on its pretend disk. */
export interface StubbedTextFile {
  /** The text. */
  content: string
  /** The modification instant the stub reports and compares saves against. */
  modifiedAt: string
  /** The permission bits. */
  mode: string
}

/** What a spec can read back from, and steer in, the stubbed file endpoints. */
export interface FileEditorStub {
  /** The bodies of every accepted or refused `PUT content`, in order. */
  saves: { content: string; expectedModifiedAt?: string }[]
  /** The path and `overwrite` flag of every `POST upload` the panel sent, in order. */
  uploads: { path: string; overwrite: boolean; bytes: number }[]
  /** The paths of every `GET download`. */
  downloads: string[]
  /** The file as the pretend disk holds it now; a spec changes it to simulate someone else's save. */
  disk: StubbedTextFile
  /** Names for which an upload without `overwrite` is refused as already existing. */
  existing: Set<string>
  /** Names whose upload is held until the spec calls {@link releaseUploads}; makes a running upload observable. */
  held: Set<string>
  /** Lets every held upload finish. */
  releaseUploads: () => void
  /** While set, a read of the file waits for it; makes the loading state observable (#456). */
  readsHeld: Promise<void> | null
}

/** The limits the stubbed server reports; small enough for a spec to cross them. */
export const STUB_FILES_OPTIONS: FilesOptions = {
  maxUploadBytes: 104857600,
  maxEditBytes: 64,
  maxDownloadBytes: 268435456,
  maxBatchItems: 200,
  trashRetentionDays: 30,
  extractMaxEntries: 100000,
  extractMaxBytes: 2147483648,
  extractMaxRatio: 100,
  archiveFormats: ['zip', 'tar.gz'],
}

/**
 * The size of the body an upload carried.
 * @param route The intercepted upload request.
 * @returns The body's length in bytes; 0 when it had none.
 */
const uploadedBytes = (route: Route): number => {
  return Buffer.byteLength(route.request().postData() ?? '')
}

/**
 * Answers a request with an RFC 7807 problem the way the panel does.
 * @param route The intercepted request.
 * @param status The HTTP status.
 * @param code The problem code.
 * @param detail The already-localized sentence.
 * @returns Resolves once the response is sent.
 */
const refuse = async (route: Route, status: number, code: string, detail: string): Promise<void> => {
  await route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title: detail, detail, status, code }),
  })
}

/**
 * Installs stateful stubs for the file manager endpoints the editor and the transfers use: the
 * options, reading and saving text (with the optimistic-concurrency check), raw upload and download.
 *
 * The save endpoint mirrors the server's rule: a save that carries an `expectedModifiedAt` different
 * from the disk's is refused with `FilesEditConflict` and writes nothing. Without that, a spec about
 * conflicts would pass against a stub that accepts everything.
 * @param page The Playwright page whose network the routes are installed on.
 * @param initial The file the pretend disk starts with.
 * @returns The handle a spec reads recorded requests from and steers the disk with.
 */
export const stubFileEditorRoutes = async (page: Page, initial: StubbedTextFile): Promise<FileEditorStub> => {
  let release: () => void = () => {return undefined}
  const gate = new Promise<void>((resolve) => {
    release = resolve
  })
  const stub: FileEditorStub = {
    saves: [],
    uploads: [],
    downloads: [],
    disk: { ...initial },
    existing: new Set<string>(),
    held: new Set<string>(),
    releaseUploads: release,
    readsHeld: null,
  }
  let revision = 0

  await page.route(/\/api\/v1\/files\/options(\?.*)?$/, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(STUB_FILES_OPTIONS) })
  })

  await page.route(/\/api\/v1\/files\/content(\?.*)?$/, async (route) => {
    if (route.request().method() === 'GET') {
      await stub.readsHeld
      if (Buffer.byteLength(stub.disk.content) > STUB_FILES_OPTIONS.maxEditBytes) {
        await refuse(route, 422, 'FilesNotEditable', 'This file is too large to edit here.')
        return
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          content: stub.disk.content,
          modifiedAt: stub.disk.modifiedAt,
          mode: stub.disk.mode,
          sizeBytes: Buffer.byteLength(stub.disk.content),
        }),
      })
      return
    }

    const body = route.request().postDataJSON() as { content: string; expectedModifiedAt?: string }
    stub.saves.push({ content: body.content, expectedModifiedAt: body.expectedModifiedAt })
    if (body.expectedModifiedAt !== undefined && body.expectedModifiedAt !== stub.disk.modifiedAt) {
      await refuse(route, 409, 'FilesEditConflict', 'The file changed on disk after you opened it.')
      return
    }
    revision += 1
    stub.disk = { ...stub.disk, content: body.content, modifiedAt: `2026-11-07T10:00:${String(revision).padStart(2, '0')}Z` }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ sizeBytes: Buffer.byteLength(body.content), modifiedAt: stub.disk.modifiedAt }),
    })
  })

  await page.route(/\/api\/v1\/files\/upload(\?.*)?$/, async (route) => {
    const url = new URL(route.request().url())
    const path = url.searchParams.get('path') ?? ''
    const overwrite = url.searchParams.get('overwrite') === 'true'
    const name = path.split('/').pop() ?? path
    if (stub.held.has(name)) {
      await gate
    }
    stub.uploads.push({ path, overwrite, bytes: uploadedBytes(route) })
    if (!overwrite && stub.existing.has(name)) {
      await refuse(route, 409, 'FilesEntryExists', 'A file with that name already exists.')
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ sizeBytes: uploadedBytes(route) }),
    })
  })

  await page.route(/\/api\/v1\/files\/download(\?.*)?$/, async (route) => {
    const path = new URL(route.request().url()).searchParams.get('path') ?? ''
    stub.downloads.push(path)
    await route.fulfill({
      status: 200,
      headers: {
        'content-type': 'application/octet-stream',
        'content-disposition': `attachment; filename*=UTF-8''${encodeURIComponent(path.split('/').pop() ?? 'file')}`,
      },
      body: stub.disk.content,
    })
  })

  return stub
}
