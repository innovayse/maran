/**
 * The five Node built-ins the end-to-end suite reads the repository with, declared here rather
 * than pulled in from `@types/node`.
 *
 * Why a declaration and not `"types": ["node"]` in `tsconfig.app.json`: that file compiles `src/`
 * and `e2e/` together, and `@vue/tsconfig` sets `types: []` on purpose so Node's GLOBALS —
 * `process`, `__dirname`, `Buffer` — never become available to browser code that would then ship
 * broken. Nothing in `src/` may use them, and this file keeps that true: it adds three module
 * shapes and not one global.
 *
 * Which specs need them: a check whose subject lives outside the SPA has to read the SPA's
 * counterpart from disk — `e2e/shell/module-landing-coverage.spec.ts` reads the backend's module
 * registry, because a check that reads only frontend files cannot observe a module added on the
 * backend, and a check that cannot observe its subject is decoration (rules/testing.md).
 *
 * Playwright runs specs in Node, so these resolve at run time whatever TypeScript is told. If
 * `@types/node` is ever added to this program, DELETE this file: two declarations of the same
 * module is a duplicate-identifier error, and the real types are better than these.
 */

declare module 'node:fs' {
  /**
   * Reads a file synchronously as text.
   * @param path Absolute path to the file.
   * @param encoding Text encoding to decode it with.
   * @returns The file's contents.
   */
  export const readFileSync: (path: string, encoding: 'utf8') => string
  /**
   * Lists the entries of a directory synchronously.
   * @param path Absolute path to the directory.
   * @returns The entries' names.
   */
  export const readdirSync: (path: string) => string[]
}

declare module 'node:fs/promises' {
  /**
   * Reads a file asynchronously as text.
   * @param path Absolute path to the file.
   * @param encoding Text encoding to decode it with.
   * @returns The file's contents.
   */
  export const readFile: (path: string, encoding: 'utf8') => Promise<string>
}

declare module 'node:url' {
  /**
   * Converts a `file:` URL to a platform-native absolute path.
   * @param url The `file:` URL, as `import.meta.url` reports it.
   * @returns The corresponding filesystem path.
   */
  export const fileURLToPath: (url: string) => string
}

declare module 'node:path' {
  /**
   * Returns the directory portion of a path.
   * @param path The path to take the directory of.
   * @returns Everything up to the last separator.
   */
  export const dirname: (path: string) => string
  /**
   * Joins path segments with the platform separator.
   * @param segments The segments to join.
   * @returns The joined path.
   */
  export const join: (...segments: string[]) => string
  /**
   * Resolves segments into an absolute path.
   * @param segments The segments to resolve, left to right.
   * @returns The absolute path.
   */
  export const resolve: (...segments: string[]) => string
}

declare module 'node:buffer' {
  /** A Node byte buffer: the type Playwright requires for an in-memory file payload. */
  export class Buffer extends Uint8Array {
    /**
     * Encodes text as bytes.
     * @param text The text to encode (UTF-8).
     * @returns A buffer holding the encoded bytes.
     */
    static from(text: string): Buffer
    /**
     * Decodes base64 text into the bytes it stands for.
     * @param encoded The base64 text, without a data-URL prefix.
     * @param encoding Always `'base64'`.
     * @returns A buffer holding the decoded bytes.
     */
    static from(encoded: string, encoding: 'base64'): Buffer
    /**
     * Counts the bytes text takes once encoded.
     * @param text The text to measure (UTF-8).
     * @returns The number of bytes.
     */
    static byteLength(text: string): number
  }
}
