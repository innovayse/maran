<script setup lang="ts">
/**
 * The persistent "You are viewing as <account or reseller> · Back" banner (issues #314, #317, #179). Drawn on every
 * page of the shell while the current access token belongs to a view-as session, and on none
 * otherwise.
 *
 * **It is drawn from what the panel reports, never from SPA state alone.** Each time the access
 * token changes — a sign-in, the start of viewing, a renewal after the view-as token expired or was
 * ended elsewhere — the banner asks `GET /api/v1/view-as` again through the view-as store, and shows
 * only what that answer holds. A view-as session that ended on the server therefore takes the banner
 * with it at the next token change, and a token that is not a view-as one can never show it.
 *
 * **Back** ends the view-as session on the server, then renews the access token from the operator's
 * own refresh cookie (which viewing never touched), and returns the operator to the screen of what they
 * were viewing: the account's page for an account, the reseller's page for a reseller. The two stores are called in turn here because a store never reaches into another
 * (rules/vue.md "State").
 */
import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import { useAuthStore } from '../../stores/auth'
import { useViewAsStore } from '../../stores/viewAs'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()
const viewAsStore = useViewAsStore()

/** The banner's sentence: which of the two kinds of subject the panel says is being viewed. */
const bannerText: ComputedRef<string> = computed((): string => {
  const viewing = viewAsStore.viewing
  if (viewing === null) {
    return ''
  }
  const params = { subject: viewing.subjectName, username: viewing.username }
  return viewing.kind === 'reseller' ? t('viewAs.bannerReseller', params) : t('viewAs.banner', params)
})

/** The region's accessible name, by the same kind. */
const regionLabel: ComputedRef<string> = computed((): string => {
  return viewAsStore.viewing?.kind === 'reseller' ? t('viewAs.regionLabelReseller') : t('viewAs.regionLabel')
})

/**
 * Ends viewing and returns the operator to their own session and to the screen of what they viewed.
 * @returns Resolves once the operator is back, or once the renewal found no session to return to.
 */
const back = async (): Promise<void> => {
  const viewing = viewAsStore.viewing
  await viewAsStore.stop()

  // The operator's own refresh cookie is the way back; viewing never wrote or deleted it.
  if (!(await authStore.renewAccessToken())) {
    return
  }

  if (viewing === null) {
    await router.push({ name: 'accounts' })
    return
  }

  // An administrator returns to the screen of what they viewed; a reseller, who only ever views
  // its own accounts, returns to the account.
  await router.push(
    viewing.kind === 'reseller'
      ? { name: 'reseller-detail', params: { id: viewing.subjectId } }
      : { name: 'account-detail', params: { id: viewing.subjectId } },
  )
}

// Every token change re-asks the panel, so the banner follows the server's session rather than a
// flag: viewing that ended elsewhere disappears at the renewal that follows it.
watch(
  () => {
    return authStore.accessToken
  },
  async (token) => {
    if (token === null) {
      viewAsStore.clear()
      return
    }

    await viewAsStore.load()
  },
  { immediate: true },
)
</script>

<template>
  <section v-if="viewAsStore.viewing !== null" :aria-label="regionLabel" data-testid="viewing-as-banner">
    <UiAlert variant="warning">
      {{ bannerText }}
      <template #actions>
        <UiButton variant="secondary" :disabled="viewAsStore.acting" @click="back">
          {{
            viewAsStore.acting
              ? t('viewAs.backing')
              : t('viewAs.back', { operator: viewAsStore.viewing.operatorUsername })
          }}
        </UiButton>
      </template>
    </UiAlert>
  </section>
</template>
