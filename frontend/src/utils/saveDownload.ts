import type { ApiDownload } from '../types/api'

/**
 * How long the object URL outlives the click, in milliseconds. Some browsers start the save
 * after the click handler returns and read the URL then, so revoking in the same turn can cancel
 * the download; a short delay still releases the blob's memory long before it matters.
 */
const REVOKE_DELAY_MS = 30_000

/**
 * Hands a downloaded file to the browser's save flow.
 *
 * The one place the SPA creates a temporary object URL and clicks an anchor: a plain link cannot
 * carry the bearer token and the CSRF header the export endpoints need, so the file is fetched as
 * a blob first and saved from memory. The URL is revoked after a delay, not in the same turn as the
 * click, because a browser may only read it once the click handler has returned.
 * @param download The file and the name the panel proposed for it.
 * @returns Nothing; the save dialog or the download folder takes over.
 */
export const saveDownload = (download: ApiDownload): void => {
  const url = URL.createObjectURL(download.blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = download.fileName
  anchor.hidden = true
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => {
    URL.revokeObjectURL(url)
  }, REVOKE_DELAY_MS)
}
