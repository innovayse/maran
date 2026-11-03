<script setup lang="ts">
/**
 * One reseller, for its administrator: who it is, what it may sell (its envelope), what it has used
 * of that, and the things that can be done to it: change the envelope, suspend or reactivate,
 * delete, and view the panel as it. Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under.
 *
 * Every lifecycle action asks first in {@link UiConfirm}, naming the reseller and what will happen.
 * Suspending states the one thing an operator would otherwise guess: the reseller's customers'
 * accounts keep running, and only the reseller's own login and its writes stop. Deleting is refused
 * by the panel while the reseller owns accounts or plans, and that sentence is shown verbatim.
 *
 * **"View as this reseller"** asks the panel for a view-as token, hands it to the auth store in place
 * of the administrator's, and lands on the reseller's overview with the "viewing as" banner above
 * it. Whether the administrator may do this is the panel's decision, never this page's, and a
 * refusal stays here in the alert below the heading.
 *
 * While the reseller loads, the heading and the cards are drawn already with their real terms and
 * placeholders, so it lands in the space held for it.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import ResellerActionsCard from '../../components/resellers/ResellerActionsCard.vue'
import ResellerDetailsCard from '../../components/resellers/ResellerDetailsCard.vue'
import ResellerEnvelopeCard from '../../components/resellers/ResellerEnvelopeCard.vue'
import ResellerUsageCard from '../../components/resellers/ResellerUsageCard.vue'
import { useAuthStore } from '../../stores/auth'
import { useLocaleStore } from '../../stores/locale'
import { usePlansStore } from '../../stores/plans'
import { useResellersStore } from '../../stores/resellers'
import { useViewAsStore } from '../../stores/viewAs'
import { takeFlashNotice } from '../../utils/flashNotice'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The reseller's identity, from `/resellers/:id`. */
  id: string
}>()

/** The one dialog that is open over the page, or `null` when none is: two questions are never asked at once. */
type ResellerDialog = 'suspend' | 'reactivate' | 'delete' | null

const { t } = useI18n()
const router = useRouter()
const store = useResellersStore()
const plansStore = usePlansStore()
const localeStore = useLocaleStore()
const authStore = useAuthStore()
const viewAsStore = useViewAsStore()

/** Which confirmation the administrator has opened. */
const dialog: Ref<ResellerDialog> = ref(null)

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the panel answered that it has no such reseller. */
const isMissing: ComputedRef<boolean> = computed(() => {
  return store.selectedState === 'loaded' && store.selected === null
})

/** Whether the reseller is still on its way, so the details draw placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return store.selectedState === 'loading'
})

/** The confirmation's title, naming the reseller and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = store.selected?.name ?? ''
  if (dialog.value === 'delete') {
    return t('resellers.detail.confirmDeleteTitle', { name })
  }
  return dialog.value === 'suspend'
    ? t('resellers.detail.confirmSuspendTitle', { name })
    : t('resellers.detail.confirmReactivateTitle', { name })
})

/** The consequence the confirmation asks the administrator to weigh. */
const confirmationText: ComputedRef<string> = computed(() => {
  if (dialog.value === 'delete') {
    return t('resellers.detail.confirmDelete')
  }
  return dialog.value === 'suspend' ? t('resellers.detail.confirmSuspend') : t('resellers.detail.confirmReactivate')
})

/** The confirm button's label, naming the action rather than a bare "Confirm". */
const confirmationLabel: ComputedRef<string> = computed(() => {
  if (dialog.value === 'delete') {
    return t('resellers.detail.confirmDeleteAction')
  }
  return dialog.value === 'suspend' ? t('resellers.detail.confirmSuspendAction') : t('resellers.detail.confirmReactivateAction')
})

/**
 * Opens a confirmation, forgetting the last refusal so it does not greet a new question.
 * @param action The action the administrator clicked.
 * @returns Nothing.
 */
const ask = (action: 'suspend' | 'reactivate' | 'delete'): void => {
  store.clearErrors()
  dialog.value = action
}

/**
 * Abandons the open question.
 * @returns Nothing.
 */
const cancel = (): void => {
  dialog.value = null
}

/**
 * Carries out the confirmed action. The dialog is closed AFTER the request settles: it is what
 * shows the administrator that something is happening. A deleted reseller leaves for the list,
 * because the page it was on no longer describes anything; a refusal stays on the page, in the alert.
 * @returns Resolves once the request has settled and any navigation is done.
 */
const confirm = async (): Promise<void> => {
  const action = dialog.value
  if (action === 'delete') {
    const deleted = await store.remove(props.id)
    dialog.value = null
    if (deleted) {
      await router.push({ name: 'resellers' })
    }
    return
  }
  if (action === 'suspend') {
    await store.suspend(props.id)
  } else if (action === 'reactivate') {
    await store.reactivate(props.id)
  }
  dialog.value = null
}

/**
 * Starts viewing the panel as this reseller's login: the panel issues a view-as token, the auth
 * store holds it in place of the administrator's, and the administrator lands on the reseller's
 * overview with the "viewing as" banner above it. A refusal stays on this page.
 * @returns Resolves once the administrator is viewing, or once the refusal is shown.
 */
const viewAs = async (): Promise<void> => {
  const session = await viewAsStore.startReseller(props.id)
  if (session === null) {
    return
  }
  authStore.adoptViewAs(session)
  await router.push({ name: 'reseller-overview' })
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  notice.value = takeFlashNotice()
  store.clearErrors()
  // The feature names are their own read: a failed one only leaves the envelope naming features by their wire name.
  await Promise.all([store.loadOne(props.id), plansStore.loadFeatureChoices()])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiLoadingRegion :state="store.selectedState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="isMissing"
        :title="t('resellers.detail.notFoundTitle')"
        :description="t('resellers.detail.notFoundDescription')"
      >
        <UiLink :to="{ name: 'resellers' }" icon="chevronLeft">{{ t('resellers.form.backToList') }}</UiLink>
      </UiEmptyState>

      <div v-else class="flex flex-col gap-4">
        <!-- The name is an identifier (it is the reseller's login), so the title is set in the mono face. -->
        <UiPageHeading :title="store.selected?.name ?? ''" :subtitle="store.selected?.contactEmail ?? ''" :loading="isLoading" mono>
          <template #actions>
            <UiLink :to="{ name: 'resellers' }" icon="chevronLeft">{{ t('resellers.form.backToList') }}</UiLink>
            <UiButton variant="secondary" :disabled="isLoading || viewAsStore.acting" @click="viewAs">
              {{ viewAsStore.acting ? t('viewAs.startingReseller') : t('viewAs.actionReseller') }}
            </UiButton>
          </template>
        </UiPageHeading>

        <!-- A refusal the panel sent for a suspend, reactivate or delete, then for "View as", verbatim. -->
        <UiAlert v-if="!isLoading && store.actionErrorMessage !== null" variant="error">
          {{ store.actionErrorMessage }}
        </UiAlert>
        <UiAlert v-if="viewAsStore.errorMessage !== null" variant="error">{{ viewAsStore.errorMessage }}</UiAlert>

        <UiAlert v-if="!isLoading && store.selected?.status === 'suspended'" variant="warning">
          {{ t('resellers.detail.suspendedNotice') }}
        </UiAlert>

        <div class="grid gap-4 lg:grid-cols-3">
          <ResellerDetailsCard class="lg:col-span-2" :reseller="store.selected" :locale="localeStore.current" />
          <ResellerActionsCard
            :reseller-id="id"
            :status="store.selected?.status ?? null"
            @suspend="ask('suspend')"
            @reactivate="ask('reactivate')"
            @delete="ask('delete')"
          />
        </div>

        <div class="grid gap-4 lg:grid-cols-3">
          <ResellerEnvelopeCard class="lg:col-span-2" :envelope="store.selected?.envelope ?? null" :feature-choices="plansStore.featureChoices" />
          <ResellerUsageCard :reseller="store.selected" />
        </div>
      </div>
    </UiLoadingRegion>

    <UiConfirm
      :open="dialog !== null"
      :title="confirmationTitle"
      :question="confirmationText"
      :confirm-label="confirmationLabel"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('resellers.detail.working')"
      :destructive="dialog !== 'reactivate'"
      @close="cancel"
      @confirm="confirm"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
