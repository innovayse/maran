import { useApi } from '../useApi'
import type { Profile, ProfileApi } from '../../types/profile'

/** The endpoint the caller's own profile is read from. */
const PROFILE_PATH = '/api/v1/profile'

/**
 * Builds the profile API on top of the shared low-level client. The endpoint is
 * scoped to the caller's own token, so no call here names a user.
 * @returns The {@link ProfileApi} bound to the panel's profile endpoint.
 */
export const useProfileApi = (): ProfileApi => {
  const api = useApi()

  /**
   * Reads the caller's own profile.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The profile.
   */
  const get = (signal?: AbortSignal): Promise<Profile> => {
    return api.get<Profile>(PROFILE_PATH, signal)
  }

  return { get }
}
