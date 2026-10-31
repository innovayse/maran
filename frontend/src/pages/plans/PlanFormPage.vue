<script setup lang="ts">
/**
 * The plan form screen, one page for both routes: `/plans/new` defines a plan and `/plans/:id`
 * edits one. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. State comes exclusively from the plans store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **An edit reads its one plan fresh** (`GET /api/v1/plans/{id}`), never the list: the list is
 * paged, so the plan a link names need not be on any page held. It is read even when the plan was
 * read before, because the account count the edit notice states is a live fact and a stale one
 * would tell an administrator that nobody is on a plan somebody has just been put on. An id the
 * panel does not know reads as "not found", with the way back to the list.
 *
 * **The form that edits is not created until the plan it edits is known.** `PlanForm` takes its
 * starting values once, when it is created, so a form mounted before the plan arrived would show
 * empty fields for a plan that exists. While the plan loads, a stand-in form with `loading` holds
 * the real labels over placeholder controls (inside a {@link UiLoadingRegion}); once the plan is
 * known, a fresh form created from it takes the stand-in's place.
 *
 * A refusal from the server — a name another plan has, a quota an account already exceeds — is
 * rendered verbatim above the form, and the form stays as the administrator left it so they can
 * correct the one figure and send it again.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import PlanForm from '../../components/plans/PlanForm.vue'
import { usePlansStore } from '../../stores/plans'
import type { ManagedPlan, PlanRequest } from '../../types/plan'
import { flashNoticeState } from '../../utils/flashNotice'

/** Props the router passes (`props: true` on the edit route; absent on the create route). */
const props = defineProps<{
  /** The plan being edited, from the path; undefined when a new plan is being defined. */
  id?: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = usePlansStore()

/** Whether this page edits an existing plan. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.id !== undefined
})

/** The plan being edited, or `undefined` while it has not arrived or the panel has no such plan. */
const plan: ComputedRef<ManagedPlan | undefined> = computed(() => {
  return props.id === undefined ? undefined : (store.selected ?? undefined)
})

/** The heading's key: it names the job the page is doing. */
const headingKey: ComputedRef<string> = computed(() => {
  return isEditing.value ? 'plans.form.headingEdit' : 'plans.form.headingNew'
})

/**
 * Prepares the screen: forgets the last attempt's refusal and, for an edit, loads the plan.
 * @returns Resolves once the load, if any, has settled.
 */
const prepare = async (): Promise<void> => {
  store.clearErrors()
  if (props.id !== undefined) {
    await store.loadOne(props.id)
  }
}

/**
 * Sends the validated request: a create or an update, then back to the list on success.
 * @param request The plan's name and limits, as the form validated them.
 * @returns Resolves once the attempt has settled; a refusal stays in the store for the template.
 */
const save = async (request: PlanRequest): Promise<void> => {
  const creating = props.id === undefined
  const saved = props.id === undefined ? await store.create(request) : await store.update(props.id, request)
  if (saved !== null) {
    // Only a create announces itself: an edit lands on a list showing the changed figures.
    await router.push(
      creating
        ? { name: 'plans', state: flashNoticeState(t('plans.form.created', { name: saved.displayName })) }
        : { name: 'plans' },
    )
  }
}

/**
 * Leaves the form without saving.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'plans' })
}

onMounted(prepare)
</script>

<template>
  <section class="flex w-full max-w-3xl flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('plans.form.eyebrow')"
      :title="t(headingKey)"
      :subtitle="isEditing ? t('plans.form.subtitleEdit') : t('plans.form.subtitleNew')"
    />

    <!-- The one thing that needs attention goes first: a refusal from the server, rendered
         verbatim — the message already says what to change. -->
    <UiAlert v-if="store.saveErrorMessage !== null" variant="error">
      {{ store.saveErrorMessage }}
    </UiAlert>

    <UiLoadingRegion v-if="isEditing" :state="store.loadState">
      <!-- A customer who opens the edit URL lands here too: the plan endpoint refused them, and
           its message is shown instead of the form. -->
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <PlanForm v-if="store.loadState === 'loading'" :plan="null" loading :submitting="store.saving" />

      <UiEmptyState
        v-else-if="plan === undefined"
        :title="t('plans.form.notFoundTitle')"
        :description="t('plans.form.notFoundDescription')"
      >
        <UiLink :to="{ name: 'plans' }" icon="chevronLeft">{{ t('plans.form.backToList') }}</UiLink>
      </UiEmptyState>

      <PlanForm v-else :plan="plan" :submitting="store.saving" @submit="save" @cancel="cancel" />
    </UiLoadingRegion>

    <PlanForm v-else :plan="null" :submitting="store.saving" @submit="save" @cancel="cancel" />
  </section>
</template>
