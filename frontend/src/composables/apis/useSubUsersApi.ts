import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  ChangeSubUserFeaturesRequest,
  GrantableFeature,
  InviteSubUserRequest,
  SubUser,
  SubUsersApi,
} from '../../types/subUser'

/** The endpoint sub-users are listed, invited, changed and removed through. */
const SUB_USERS_PATH = '/api/v1/me/sub-users'

/** The sub-users list takes no filter, only a page, a search and a sort. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the sub-users API on top of the shared low-level client.
 *
 * The account is never named: every call acts on the account the caller's own sign-in speaks for, so
 * there is no id in any path or body with which to reach another account's team.
 * @returns The {@link SubUsersApi} bound to the panel's sub-user endpoints.
 */
export const useSubUsersApi = (): SubUsersApi => {
  const api = useApi()

  /**
   * Lists one page of the caller's sub-users.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<SubUser>> => {
    return api.get<Paged<SubUser>>(`${SUB_USERS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists the features an owner may give.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every grantable feature with its label.
   */
  const features = (signal?: AbortSignal): Promise<GrantableFeature[]> => {
    return api.get<GrantableFeature[]>(`${SUB_USERS_PATH}/features`, signal)
  }

  /**
   * Invites a person to the account.
   * @param request Their login name, address and features.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The new sub-user.
   */
  const invite = (request: InviteSubUserRequest, signal?: AbortSignal): Promise<SubUser> => {
    return api.post<SubUser>(SUB_USERS_PATH, request, signal)
  }

  /**
   * Replaces a sub-user's features.
   * @param id The sub-user to change.
   * @param request The complete new set of feature ids.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sub-user as they now stand.
   */
  const changeFeatures = (
    id: string,
    request: ChangeSubUserFeaturesRequest,
    signal?: AbortSignal,
  ): Promise<SubUser> => {
    return api.put<SubUser>(`${SUB_USERS_PATH}/${id}/features`, request, signal)
  }

  /**
   * Ends a sub-user's access to the account.
   * @param id The sub-user to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel ended the access; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${SUB_USERS_PATH}/${id}`, signal)
  }

  return { list, features, invite, changeFeatures, remove }
}
