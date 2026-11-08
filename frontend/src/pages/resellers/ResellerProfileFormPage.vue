<script setup lang="ts">
/**
 * The screen that renames a reseller and changes its contact address and language
 * (`/resellers/:id/profile`). Renders a `<section>`, not a `<main>` — the single `<main>` landmark
 * lives in the layout this page is nested under. State comes exclusively from the stores; the page
 * never touches the API layer (rules/vue.md).
 *
 * **The form is not created until the reseller it edits is known and the languages have arrived**,
 * because the form takes its starting values once, when it is created: a form mounted earlier would
 * show empty fields for a reseller that exists. While they load, a placeholder is drawn.
 *
 * A refusal from the server (a name another login holds, an address it will not take) is rendered
 * verbatim above the form, and the form stays as the administrator left it. The reseller's login
 * follows the change, so the notice on the detail page says so.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import ResellerProfileForm from '../../components/resellers/ResellerProfileForm.vue'
import { useOwnerLanguagesStore } from '../../stores/ownerLanguages'
import { useResellersStore } from '../../stores/resellers'
import type { ResellerDetail, UpdateResellerRequest } from '../../types/reseller'
import { flashNoticeState } from '../../utils/flashNotice'

/** Props the router passes. */
const props = defineProps<{
  /** The reseller being edited, from `/resellers/:id/profile`. */
  id: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useResellersStore()
const languagesStore = useOwnerLanguagesStore()

/** The reseller being edited, or `undefined` while it has not arrived or the panel has no such reseller. */
const reseller: ComputedRef<ResellerDetail | undefined> = computed(() => {
  return store.selected ?? undefined
})

/**
 * Prepares the screen: forgets the last attempt's refusal and reads what the form needs. The reads
 * are independent, so they go out together.
 * @returns Resolves once every read has settled.
 */
const prepare = async (): Promise<void> => {
  store.clearErrors()
  await Promise.all([languagesStore.load(), store.loadOne(props.id)])
}

/**
 * Saves the change and goes back to the reseller's page.
 * @param request The new name, contact address and language as the form validated them.
 * @returns Resolves once the attempt has settled; a refusal stays in the store for the template.
 */
const save = async (request: UpdateResellerRequest): Promise<void> => {
  const saved = await store.update(props.id, request)
  if (saved !== null) {
    await router.push({
      name: 'reseller-detail',
      params: { id: props.id },
      state: flashNoticeState(t('resellers.profile.saved', { name: saved.name })),
    })
  }
}

/**
 * Leaves the form without saving.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'reseller-detail', params: { id: props.id } })
}

onMounted(prepare)
</script>

<template>
  <section class="flex w-full max-w-4xl flex-col gap-4">
    <UiPageHeading :title="t('resellers.profile.heading')" :subtitle="t('resellers.profile.subtitle')" />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered verbatim. -->
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">{{ store.saveErrorMessage }}</UiAlert>

    <UiLoadingRegion :state="store.selectedState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <!-- The region draws its content while loading too, so the wait is a placeholder and never
           a "not found" that has not been answered yet. -->
      <UiSkeleton v-if="store.selectedState === 'loading' || languagesStore.languagesState === 'loading'" shape="block" />

      <UiEmptyState
        v-else-if="reseller === undefined"
        :title="t('resellers.form.notFoundTitle')"
        :description="t('resellers.form.notFoundDescription')"
      >
        <UiLink :to="{ name: 'resellers' }" icon="chevronLeft">{{ t('resellers.form.backToList') }}</UiLink>
      </UiEmptyState>

      <ResellerProfileForm
        v-else
        :reseller="reseller"
        :submitting="store.saving"
        :languages="languagesStore.languages"
        :languages-error="languagesStore.errorMessage"
        @submit="save"
        @cancel="cancel"
      />
    </UiLoadingRegion>
  </section>
</template>
