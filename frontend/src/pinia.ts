import { createPinia, type Pinia  } from 'pinia'

/**
 * Creates the application's single Pinia instance. Feature modules define
 * one setup-style store per module and register it against this instance
 * via `useXStore()`; nothing here holds application state itself.
 * @returns A fresh {@link Pinia} instance to install with `app.use()`.
 */
export const createAppPinia = (): Pinia => createPinia()
