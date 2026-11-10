import type { InjectionKey, Ref } from 'vue'

/**
 * What the signed-in shell provides to the kit: whether the current session is read-only, which it
 * is exactly while an operator is viewing as another login (issues #334 and #515). `UiForm` and
 * `UiButton` read it through `useSessionReadOnly` and withhold what writes: a form's fields, a
 * submit button and a destructive button are drawn disabled, instead of being filled in and refused
 * only after submit. The value is advice drawn from what the panel reported; the panel refuses every
 * write of such a session on its own, whatever the browser shows.
 */
export const SESSION_READ_ONLY_KEY: InjectionKey<Readonly<Ref<boolean>>> = Symbol('session-read-only')
