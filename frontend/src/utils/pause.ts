/**
 * Waits for a number of milliseconds. The one place a store or composable that polls on a schedule
 * gets its delay from, so no caller hand-builds a timer promise.
 * @param milliseconds How long to wait.
 * @returns Resolves once the time has passed.
 */
export const pause = (milliseconds: number): Promise<void> => {
  return new Promise<void>((resolve) => {
    setTimeout(resolve, milliseconds)
  })
}
