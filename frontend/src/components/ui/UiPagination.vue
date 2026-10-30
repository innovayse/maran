<script setup lang="ts">
/**
 * The page controls under a server-paged table: which rows are showing, a way to every page, and
 * the page size.
 *
 * **Two layouts.** From `sm` up: "Showing 26–50 of 312", then first / previous / numbered pages /
 * next / last, then a page-size select. Below `sm` there is room for one line only: previous,
 * "Page 2 of 13", next. The two are swapped with CSS (`hidden` / `sm:`), so the hidden one is out of
 * the accessibility tree as well as off screen, and there is no script deciding the layout.
 *
 * **Numbered pages.** The first, the last and the current page with one neighbour each side; a gap
 * of one page is shown as that page rather than an ellipsis, since the ellipsis would be no shorter.
 *
 * **Accessibility.** A `nav` landmark named by `labels.navigation`; the current page carries
 * `aria-current="page"`; every icon-only control is named by its label; a control that leads
 * nowhere (previous on page 1) is disabled rather than hidden, so the row does not jump.
 *
 * The component is dumb: it reports `update:page` and `update:pageSize`, and the caller (normally
 * `useServerTable`) writes the URL. Every text is a required prop, already translated.
 */
import { computed, type ComputedRef } from 'vue'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'
import UiSelect, { type SelectOption } from './UiSelect.vue'

/** The translated texts {@link UiPagination} shows; every one is required. */
export interface PaginationLabels {
  /** The landmark's accessible name ("Pagination"). */
  navigation: string
  /** The first-page control's name. */
  first: string
  /** The previous-page control's name. */
  previous: string
  /** The next-page control's name. */
  next: string
  /** The last-page control's name. */
  last: string
  /** The page-size select's label ("Rows per page"). */
  pageSize: string
  /** A numbered page's accessible name ("Page 4"). */
  page: (page: number) => string
  /** The phone layout's position ("Page 2 of 13"). */
  position: (page: number, pages: number) => string
  /** The rows showing ("Showing 26–50 of 312"). */
  range: (from: number, to: number, total: number) => string
}

/** One slot in the numbered row: a page, or a gap standing for the pages left out. */
type PageSlot = { kind: 'page'; page: number } | { kind: 'gap'; key: string }

/** Props accepted by {@link UiPagination}. */
const props = defineProps<{
  /** The 1-based current page. */
  page: number
  /** Rows per page. */
  pageSize: number
  /** Rows across every page. */
  total: number
  /** The page sizes offered in the select. */
  pageSizes: readonly number[]
  /** Every text the component shows, already translated. */
  labels: PaginationLabels
}>()

/** Events emitted by {@link UiPagination}. */
const emit = defineEmits<{
  /** The reader chose a page. */
  (e: 'update:page', page: number): void
  /** The reader chose a page size. */
  (e: 'update:pageSize', pageSize: number): void
}>()

/** How many pages the total fills; at least one. */
const pageCount: ComputedRef<number> = computed(() => {
  return Math.max(1, Math.ceil(props.total / props.pageSize))
})

/** The first row's 1-based position on this page; 0 when there are no rows. */
const firstRow: ComputedRef<number> = computed(() => {
  return props.total === 0 ? 0 : Math.min(props.total, (props.page - 1) * props.pageSize + 1)
})

/** The last row's 1-based position on this page. */
const lastRow: ComputedRef<number> = computed(() => {
  return Math.min(props.total, props.page * props.pageSize)
})

/** Whether there is a page before this one. */
const hasPrevious: ComputedRef<boolean> = computed(() => {
  return props.page > 1
})

/** Whether there is a page after this one. */
const hasNext: ComputedRef<boolean> = computed(() => {
  return props.page < pageCount.value
})

/** The numbered row: first, last, the current page and its neighbours, with gaps between. */
const pageSlots: ComputedRef<PageSlot[]> = computed(() => {
  const last = pageCount.value
  const wanted = new Set([1, last, props.page - 1, props.page, props.page + 1])
  const pages = [...wanted].filter((page) => {return page >= 1 && page <= last}).sort((a, b) => {return a - b})
  const result: PageSlot[] = []
  let previous = 0
  for (const page of pages) {
    if (page - previous === 2) {
      // A gap of exactly one page: the page itself is no wider than an ellipsis.
      result.push({ kind: 'page', page: page - 1 })
    } else if (page - previous > 2) {
      result.push({ kind: 'gap', key: `gap-${previous}` })
    }
    result.push({ kind: 'page', page })
    previous = page
  }
  return result
})

/** The page-size select's options. */
const sizeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.pageSizes.map((size) => {return { value: String(size), label: String(size) }})
})

/**
 * Reports a page choice, unless it is the page already showing.
 * @param target The chosen page.
 * @returns Nothing.
 */
const goTo = (target: number): void => {
  if (target !== props.page && target >= 1 && target <= pageCount.value) {
    emit('update:page', target)
  }
}

/**
 * Reports a page-size choice from the select.
 * @param value The chosen size, as the select's string value.
 * @returns Nothing.
 */
const choosePageSize = (value: string): void => {
  const size = Number(value)
  if (size !== props.pageSize) {
    emit('update:pageSize', size)
  }
}
</script>

<template>
  <nav :aria-label="labels.navigation" class="flex flex-wrap items-end justify-between gap-3" data-testid="pagination">
    <p class="hidden text-sm text-text-secondary sm:block" data-testid="pagination-range">
      {{ labels.range(firstRow, lastRow, total) }}
    </p>

    <ul class="hidden items-center gap-1 sm:flex" data-testid="pagination-pages">
      <li>
        <UiButton variant="ghost" size="icon" :disabled="!hasPrevious" :aria-label="labels.first" @click="goTo(1)">
          <UiIcon name="chevronsLeft" size="sm" />
        </UiButton>
      </li>
      <li>
        <UiButton
          variant="ghost"
          size="icon"
          :disabled="!hasPrevious"
          :aria-label="labels.previous"
          @click="goTo(page - 1)"
        >
          <UiIcon name="chevronLeft" size="sm" />
        </UiButton>
      </li>
      <li v-for="entry in pageSlots" :key="entry.kind === 'page' ? entry.page : entry.key">
        <UiButton
          v-if="entry.kind === 'page'"
          :variant="entry.page === page ? 'secondary' : 'ghost'"
          size="icon"
          :aria-label="labels.page(entry.page)"
          :aria-current="entry.page === page ? 'page' : undefined"
          @click="goTo(entry.page)"
        >
          {{ entry.page }}
        </UiButton>
        <UiIcon v-else name="ellipsis" size="sm" class="mx-1 text-text-muted" />
      </li>
      <li>
        <UiButton variant="ghost" size="icon" :disabled="!hasNext" :aria-label="labels.next" @click="goTo(page + 1)">
          <UiIcon name="chevronRight" size="sm" />
        </UiButton>
      </li>
      <li>
        <UiButton
          variant="ghost"
          size="icon"
          :disabled="!hasNext"
          :aria-label="labels.last"
          @click="goTo(pageCount)"
        >
          <UiIcon name="chevronsRight" size="sm" />
        </UiButton>
      </li>
    </ul>

    <div class="hidden w-40 sm:block">
      <UiSelect
        :model-value="String(pageSize)"
        :label="labels.pageSize"
        :options="sizeOptions"
        @update:model-value="choosePageSize"
      />
    </div>

    <div class="flex w-full items-center justify-between gap-2 sm:hidden" data-testid="pagination-compact">
      <UiButton
        variant="secondary"
        size="icon"
        :disabled="!hasPrevious"
        :aria-label="labels.previous"
        @click="goTo(page - 1)"
      >
        <UiIcon name="chevronLeft" size="sm" />
      </UiButton>
      <p class="min-w-0 text-center text-sm text-text-secondary wrap-anywhere">
        {{ labels.position(page, pageCount) }}
      </p>
      <UiButton variant="secondary" size="icon" :disabled="!hasNext" :aria-label="labels.next" @click="goTo(page + 1)">
        <UiIcon name="chevronRight" size="sm" />
      </UiButton>
    </div>
  </nav>
</template>
