/**
 * Where a read of server data stands: still on its way, arrived, or failed.
 *
 * Every failure path of a read sets `error` explicitly, whatever the cause (a refusal, an
 * unreachable panel), so a screen can never be left waiting on a read that has already ended.
 */
export type LoadingState = 'loading' | 'loaded' | 'error'
