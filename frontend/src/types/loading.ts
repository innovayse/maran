/**
 * Where a read of server data stands: still on its way, arrived, or failed.
 *
 * Every failure path of a read sets `error` explicitly, whatever the cause (a refusal, an
 * unreachable panel), so a screen can never be left waiting on a read that has already ended.
 */
export type LoadingState = 'loading' | 'loaded' | 'error'

/**
 * A read of server data as one value: not asked yet, on its way, arrived with its data, or failed.
 *
 * A union and not a value beside flags: only the `loaded` arm has `value`, and only the `failed`
 * arm has `message`, so a screen cannot read data a failed read never produced, and nothing stands
 * in for "no answer" (not an empty list, not a zero, not a `null`). Narrow on `state` first.
 * @template T What a successful read delivers.
 */
export type Read<T> =
  | {
      /** Nothing has asked yet. */
      state: 'idle'
    }
  | {
      /** The request is in flight. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'loaded'
      /** What it answered with. */
      value: T
    }
  | {
      /** The read ended without an answer. */
      state: 'failed'
      /** The panel's reason, already localized; `null` when no answer arrived at all. */
      message: string | null
    }

/**
 * Where one user-started action (a save, a test send) stands: untouched, in flight, done, or
 * refused. One value instead of an in-flight flag, a "saved" flag and a message that each had to
 * be reset by hand on the next attempt; only the `failed` arm has a message.
 */
export type Submission =
  | {
      /** Nothing has been sent since the screen opened or the last attempt began. */
      state: 'idle'
    }
  | {
      /** The request is in flight. */
      state: 'sending'
    }
  | {
      /** The panel accepted it. */
      state: 'done'
    }
  | {
      /** The attempt ended without being accepted. */
      state: 'failed'
      /** The panel's reason, already localized; `null` when no answer arrived at all. */
      message: string | null
    }

/**
 * The two calls a singleton setting is read and written with: `get` reads the whole setting and
 * `save` replaces it. Both alert settings (the recipients, the thresholds) have exactly this shape.
 */
export interface StoredSettingApi<TValue, TRequest> {
  /** Reads the setting as the panel holds it. */
  get: () => Promise<TValue>
  /** Replaces the setting; the panel answers with no body. */
  save: (request: TRequest) => Promise<void>
}
