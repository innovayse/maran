/// <reference types="vite/client" />

/**
 * True only in the public demo's build (`VITE_MARAN_DEMO=1`, see `vite.config.ts`). It is replaced
 * by a literal at build time, so every `__MARAN_DEMO__ ? … : null` in the normal build folds to
 * `null` and the demo components behind it are never bundled.
 */
declare const __MARAN_DEMO__: boolean
