import type { LoadingState, Read } from '../types/loading'

/**
 * Names where a {@link Read} stands in the three words the loading region understands.
 *
 * A read nobody has asked for yet counts as `loading`: a screen that is about to ask draws its
 * placeholder, not an empty result.
 * @param read The read to describe.
 * @returns `loaded` once the panel answered, `error` once the read failed, otherwise `loading`.
 */
export const loadingStateOf = <T>(read: Read<T>): LoadingState => {
  if (read.state === 'loaded') {
    return 'loaded'
  }

  return read.state === 'failed' ? 'error' : 'loading'
}
