<script setup lang="ts">
/**
 * The reseller form screen, one page for both routes: `/resellers/new` creates a reseller and
 * `/resellers/:id/edit` changes the envelope of one. Renders a `<section>`, not a `<main>` — the
 * single `<main>` landmark lives in the layout this page is nested under. State comes exclusively
 * from the stores; the page never touches the API layer (rules/vue.md).
 *
 * **An edit reads its one reseller fresh**, never the list, because the list is paged and the link
 * names one reseller. **The form that edits is not created until the reseller it edits is known**:
 * the form takes its starting values once, when it is created, so a form mounted before the reseller
 * arrived would show empty fields for a reseller that exists. While it loads, a stand-in for the page
 * is the loading region; once it is known, the form is created from it.
 *
 * **A new reseller's form waits for the languages**, because it picks its starting language once, when
 * it is created.
 *
 * A refusal from the server (a name another login holds, an envelope figure it will not take) is
 * rendered verbatim above the form, and the form stays as the administrator left it.
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
import ResellerForm from '../../components/resellers/ResellerForm.vue'
import { useLocaleStore } from '../../stores/locale'
import { useOwnerLanguagesStore } from '../../stores/ownerLanguages'
import { usePlansStore } from '../../stores/plans'
import { useResellersStore } from '../../stores/resellers'
import type { ChangeResellerEnvelopeRequest, CreateResellerRequest, ResellerDetail } from '../../types/reseller'
import { flashNoticeState } from '../../utils/flashNotice'

/** Props the router passes (`props: true` on the edit route; absent on the create route). */
const props = defineProps<{
  /** The reseller being edited, from the path; undefined when a new reseller is being created. */
  id?: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useResellersStore()
const plansStore = usePlansStore()
const languagesStore = useOwnerLanguagesStore()
const localeStore = useLocaleStore()

/** Whether this page edits an existing reseller. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.id !== undefined
})

/** The reseller being edited, or `undefined` while it has not arrived or the panel has no such reseller. */
const reseller: ComputedRef<ResellerDetail | undefined> = computed(() => {
  return props.id === undefined ? undefined : (store.selected ?? undefined)
})

/**
 * The language the invitation starts in: the operator's own when the panel lists it, otherwise the
 * panel's first. Only the panel says which languages an invitation may be written in.
 */
const defaultLanguage: ComputedRef<string> = computed(() => {
  const listed = languagesStore.languages.some((choice) => {
    return choice.language === localeStore.current
  })
  return listed ? localeStore.current : (languagesStore.languages[0]?.language ?? localeStore.current)
})

/**
 * Prepares the screen: forgets the last attempt's refusal and reads what the form needs. The
 * reads are independent, so they go out together.
 * @returns Resolves once every read has settled.
 */
const prepare = async (): Promise<void> => {
  store.clearErrors()
  await Promise.all([
    plansStore.loadFeatureChoices(),
    languagesStore.load(),
    props.id === undefined ? undefined : store.loadOne(props.id),
  ])
}

/**
 * Creates the reseller and opens its page, where its invitation is the next thing that happens.
 * @param request The reseller as the form validated it.
 * @returns Resolves once the attempt has settled; a refusal stays in the store for the template.
 */
const create = async (request: CreateResellerRequest): Promise<void> => {
  const created = await store.create(request)
  if (created !== null) {
    await router.push({
      name: 'reseller-detail',
      params: { id: created.id },
      state: flashNoticeState(t('resellers.form.created', { name: created.name })),
    })
  }
}

/**
 * Replaces the opened reseller's envelope and goes back to its page.
 * @param request The new envelope as the form validated it.
 * @returns Resolves once the attempt has settled; a refusal stays in the store for the template.
 */
const changeEnvelope = async (request: ChangeResellerEnvelopeRequest): Promise<void> => {
  if (props.id === undefined) {
    return
  }
  const saved = await store.changeEnvelope(props.id, request)
  if (saved !== null) {
    await router.push({
      name: 'reseller-detail',
      params: { id: props.id },
      state: flashNoticeState(t('resellers.form.envelopeSaved')),
    })
  }
}

/**
 * Leaves the form without saving.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  if (props.id === undefined) {
    await router.push({ name: 'resellers' })
    return
  }
  await router.push({ name: 'reseller-detail', params: { id: props.id } })
}

onMounted(prepare)
</script>

<template>
  <section class="flex w-full max-w-4xl flex-col gap-4">
    <UiPageHeading
      :title="isEditing ? t('resellers.form.headingEdit') : t('resellers.form.headingNew')"
      :subtitle="isEditing ? t('resellers.form.subtitleEdit') : t('resellers.form.subtitleNew')"
    />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered verbatim. -->
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">{{ store.saveErrorMessage }}</UiAlert>

    <UiLoadingRegion v-if="isEditing" :state="store.selectedState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <!-- The region draws its content while loading too, so the wait is a placeholder and never
           a "not found" that has not been answered yet. -->
      <UiSkeleton v-if="store.selectedState === 'loading'" shape="block" />

      <UiEmptyState
        v-else-if="reseller === undefined"
        :title="t('resellers.form.notFoundTitle')"
        :description="t('resellers.form.notFoundDescription')"
      >
        <UiLink :to="{ name: 'resellers' }" icon="chevronLeft">{{ t('resellers.form.backToList') }}</UiLink>
      </UiEmptyState>

      <ResellerForm
        v-else
        :reseller="reseller"
        :submitting="store.saving"
        :languages="languagesStore.languages"
        :languages-error="languagesStore.errorMessage"
        :default-language="defaultLanguage"
        :feature-choices="plansStore.featureChoices"
        :features-state="plansStore.featuresState"
        @submit-envelope="changeEnvelope"
        @cancel="cancel"
      />
    </UiLoadingRegion>

    <ResellerForm
      v-else-if="languagesStore.languagesState !== 'loading'"
      :reseller="null"
      :submitting="store.saving"
      :languages="languagesStore.languages"
      :languages-error="languagesStore.errorMessage"
      :default-language="defaultLanguage"
      :feature-choices="plansStore.featureChoices"
      :features-state="plansStore.featuresState"
      @submit="create"
      @cancel="cancel"
    />
  </section>
</template>
