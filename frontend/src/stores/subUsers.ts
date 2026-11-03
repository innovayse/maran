import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSubUsersApi } from '../composables/apis/useSubUsersApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  ChangeSubUserFeaturesRequest,
  GrantableFeature,
  InviteSubUserRequest,
  OfferMembershipRequest,
  SubUser,
} from '../types/subUser'

/**
 * Owns one page of the account's sub-users, the features an owner may give, and the invite, change
 * resend, offer and remove workflows. The sub-users page reads state from here and calls its actions — it never
 * touches the API composable directly (rules/vue.md: "API composables are called from Pinia stores
 * ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useSubUsersStore = defineStore('subUsers', () => {
  const api = useSubUsersApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const subUsers: Ref<SubUser[]> = ref([])

  /** The features an owner may give, with their labels; empty until {@link loadFeatures} answers. */
  const features: Ref<GrantableFeature[]> = ref([])

  /** True while an invite request is in flight. */
  const inviting: Ref<boolean> = ref(false)

  /** True while an offer to an existing login is in flight. */
  const offering: Ref<boolean> = ref(false)

  /** True while a resend, a change or a removal is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed invite, or `null`. */
  const inviteErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed offer, or `null`. */
  const offerErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed resend, change or removal, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed features read, or `null`. */
  const featuresErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the list into {@link subUsers} and reports how the read ended, for the page's
   * `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<SubUser>> => {
    try {
      const page = await api.list(request)
      subUsers.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads the features an owner may give. Called once when the page opens; the answer is the same
   * for every request in the session.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadFeatures = async (): Promise<void> => {
    try {
      features.value = await api.features()
      featuresErrorMessage.value = null
    } catch (error) {
      featuresErrorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Invites a person. The held page is not patched: where the new row falls depends on the server's
   * sort and search, so the page reads its list again.
   * @param request Their login name, address and features.
   * @returns True when the panel added them — the caller reads {@link inviteErrorMessage} for the
   * reason when it did not.
   */
  const invite = async (request: InviteSubUserRequest): Promise<boolean> => {
    inviting.value = true
    try {
      await api.invite(request)
      inviteErrorMessage.value = null
      return true
    } catch (error) {
      // Validation and conflict errors arrive already localized; stored verbatim rather than
      // replaced with frontend copy that would hide the reason.
      inviteErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      inviting.value = false
    }
  }

  /**
   * Mails a fresh invitation to a sub-user who has not accepted yet. The row does not change, so the
   * held page is left as it is.
   * @param id The invited sub-user.
   * @returns True when the panel sent it — the caller reads {@link actionErrorMessage} when it did not.
   */
  const resendInvitation = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.resendInvitation(id)
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Offers a person who already has a login a place on the team. The answer carries nothing about
   * whether the login exists, so this only reports whether the request was accepted; the page reads
   * its list again, where a stored offer shows as a row.
   * @param request The identifier the owner typed and the features to offer.
   * @returns True when the panel accepted the request — the caller reads {@link offerErrorMessage}
   * for the reason when it did not.
   */
  const offerExisting = async (request: OfferMembershipRequest): Promise<boolean> => {
    offering.value = true
    try {
      await api.offerExisting(request)
      offerErrorMessage.value = null
      return true
    } catch (error) {
      // The owner's own refusals (cap reached, bad input, not the owner) arrive already localized.
      offerErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      offering.value = false
    }
  }

  /**
   * Replaces a sub-user's features and patches the held row with the panel's answer.
   * @param id The sub-user to change.
   * @param request The complete new set of feature ids.
   * @returns True when the panel changed them.
   */
  const changeFeatures = async (id: string, request: ChangeSubUserFeaturesRequest): Promise<boolean> => {
    acting.value = true
    try {
      const changed = await api.changeFeatures(id, request)
      subUsers.value = subUsers.value.map((row) => {
        return row.id === id ? changed : row
      })
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Ends a sub-user's access, dropping the row from the held page; the page then reads the list again
   * so it refills from the next one.
   * @param id The sub-user to remove.
   * @returns True when the panel ended the access.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      subUsers.value = subUsers.value.filter((row) => {
        return row.id !== id
      })
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    subUsers,
    features,
    inviting,
    offering,
    acting,
    inviteErrorMessage,
    offerErrorMessage,
    actionErrorMessage,
    featuresErrorMessage,
    list,
    loadFeatures,
    invite,
    resendInvitation,
    offerExisting,
    changeFeatures,
    remove,
  }
})
