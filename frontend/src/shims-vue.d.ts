/**
 * Ambient module declaration for `.vue` single-file components.
 *
 * `vue-tsc` (used for the actual type-check gate, `npm run build`)
 * understands `.vue` imports natively via its Volar-based compiler wrapper
 * and does not need this file. The plain TypeScript program that
 * typescript-eslint's type-aware rules run against does need it, though —
 * without an ambient declaration for the `*.vue` extension, imports like
 * `import App from './App.vue'` type as `any`/error, which then trips
 * `@typescript-eslint/no-unsafe-assignment` and friends everywhere a
 * component is imported (`main.ts`, `router/index.ts`, `*.spec.ts`).
 */
declare module '*.vue' {
  import type { DefineComponent } from 'vue'

  /** The component's default export, typed loosely since the real shape is resolved per-file by vue-tsc. */
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}
