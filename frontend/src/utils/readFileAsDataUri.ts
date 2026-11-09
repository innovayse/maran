/**
 * Reads a chosen file into a base64 data URI, the form in which the panel stores and serves a logo.
 * @param file The file the person chose.
 * @returns The file as a data URI; rejects when the browser cannot read it; the
 * rejection's text is for developers only and is never shown (the caller says it in the user's language).
 */
export const readFileAsDataUri = (file: File): Promise<string> => {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = (): void => {
      // `readAsDataURL` always produces text; the check narrows the union rather than stringifying it.
      if (typeof reader.result === 'string') {
        resolve(reader.result)
      } else {
        reject(new Error('FileReader returned no text'))
      }
    }
    reader.onerror = (): void => {
      reject(new Error('FileReader failed'))
    }
    reader.readAsDataURL(file)
  })
}
