<script setup lang="ts">
/**
 * A card of shortcuts: one row per screen worth reaching in a single click from the landing page.
 *
 * Shared by the two audiences the landing page has. The administrator's card is the build-a-server
 * sequence (account, site, database); the customer's is the way into their own account and the
 * things it holds. The rows and their behaviour are identical, so one component draws both and the
 * page decides only WHICH rows, from the licence and the caller's role.
 *
 * Each row is the redesign's quick-action row: `--r-control` corners, a neutral icon square that
 * takes the accent on hover (the accent tile is reserved for marking what a block is about, and a
 * row is a control), and a chevron saying it navigates. The label wraps anywhere so a long Armenian
 * verb phrase stays inside a half-width card at 390px. Nothing here reads a store: whether a row
 * is offered is already decided by the caller, so no row can end at the upgrade page.
 */
import { RouterLink } from 'vue-router'
import UiCard from '../ui/UiCard.vue'
import type { QuickAction } from '../../types/quickAction'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

defineProps<{
  /** The card's heading, already translated by the caller. */
  title: string
  /** The rows, in the order they are drawn. */
  actions: QuickAction[]
}>()
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="title" />
    <ul class="m-0 flex list-none flex-col gap-2.5 p-0">
      <li v-for="action in actions" :key="action.key">
        <RouterLink
          :to="action.to"
          class="group flex items-center gap-3.5 rounded-control border border-border-subtle bg-surface-2 py-2.5 pr-3.5 pl-2.5 text-sm font-medium text-text-secondary transition-colors hover:border-accent/45 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
        >
          <span class="grid size-8 shrink-0 place-items-center rounded-control bg-surface-3 text-text-secondary transition-colors group-hover:bg-accent-soft group-hover:text-accent-text">
            <UiIcon :name="action.icon" />
          </span>
          <span class="min-w-0 flex-1 break-words">{{ action.label }}</span>
          <UiIcon name="chevronRight" class="text-text-muted" />
        </RouterLink>
      </li>
    </ul>
  </UiCard>
</template>
