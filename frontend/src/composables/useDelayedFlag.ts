import { onScopeDispose, readonly, ref, watch, type Ref, type WatchSource } from 'vue'

/**
 * Follows a boolean, but turns on only after it has stayed on for `delayMs`; it turns off at once.
 *
 * A loading placeholder that appears for an answer arriving in 50 ms is a flash, which reads as a
 * glitch rather than as progress. Gating its visibility on this flag shows it only for a wait long
 * enough to notice. The delay applies to the rising edge only: the moment the source turns off the
 * flag follows, so real content is never held back. A pending timer is cleared when the source
 * turns off and when the scope that owns it is disposed, so an unmounted component leaves nothing
 * behind to fire.
 * @param source The flag to follow (a ref, a computed or a getter).
 * @param delayMs How long the source must stay on before the flag does, in milliseconds.
 * @returns A read-only flag: `true` once the source has been on for `delayMs`.
 */
export const useDelayedFlag = (source: WatchSource<boolean>, delayMs = 150): Readonly<Ref<boolean>> => {
  const flag: Ref<boolean> = ref(false)

  /** The pending rise, or `undefined` when none is waiting. */
  let timer: ReturnType<typeof setTimeout> | undefined

  /**
   * Drops a rise that has not happened yet.
   * @returns Nothing.
   */
  const cancel = (): void => {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
  }

  watch(
    source,
    (on: boolean): void => {
      cancel()
      if (!on) {
        flag.value = false
        return
      }
      timer = setTimeout((): void => {
        timer = undefined
        flag.value = true
      }, delayMs)
    },
    { immediate: true },
  )

  onScopeDispose(cancel)

  return readonly(flag)
}
