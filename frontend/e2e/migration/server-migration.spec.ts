import { expect, test } from '../fixtures/guarded-test'
import { CONFLICT_REPORT, openMigrationJobs, openMigrationJobsList } from '../fixtures/stub-migration-jobs'

// Whole-server migration jobs (issue #197, phase 2). Every screen is reached by clicking, from the sidebar down;
// the stub holds the panel's state, so a spec reads back what the page sent. The SPA's checks are advice (the
// panel enforces), so the dry run is read-only and each refusing control has an inverse.

test.describe('the migrations list', () => {
  test('lists the migration and opens it by its name', async ({ page }) => {
    await openMigrationJobs(page)
    await openMigrationJobsList(page)

    await page.getByRole('link', { name: 'Move the old server' }).click()

    await expect(page.getByRole('heading', { level: 1, name: 'Move the old server' })).toBeVisible()
    await expect(page.getByText('alice', { exact: true })).toBeVisible()
  })
})

test.describe('the dry run', () => {
  test('shows the conflict the panel found and sends nothing but the dry-run request', async ({ page }) => {
    const state = await openMigrationJobs(page)
    await openMigrationJobsList(page)
    await page.getByRole('link', { name: 'Move the old server' }).click()

    await page.getByRole('button', { name: 'Dry run' }).click()

    const report = page.getByTestId('dry-run-report')
    await expect(report.getByText(CONFLICT_REPORT.findings[0]?.message ?? '')).toBeVisible()
    await expect(report.getByText('There are conflicts.', { exact: false })).toBeVisible()
    const writes = state.requests.filter((request) => {
      return request.method !== 'GET' && !request.path.endsWith('/dry-run')
    })
    expect(writes).toHaveLength(0)
  })

  test('says the migration can start when the panel found no conflict', async ({ page }) => {
    await openMigrationJobs(page, { report: { jobId: CONFLICT_REPORT.jobId, canStart: true, findings: [] } })
    await openMigrationJobsList(page)
    await page.getByRole('link', { name: 'Move the old server' }).click()

    await page.getByRole('button', { name: 'Dry run' }).click()

    await expect(page.getByTestId('dry-run-report').getByText('No conflicts. The migration can start.')).toBeVisible()
  })
})

test.describe('starting and pausing', () => {
  test('starts the draft and then offers to pause it, sending the action the button names', async ({ page }) => {
    const state = await openMigrationJobs(page)
    await openMigrationJobsList(page)
    await page.getByRole('link', { name: 'Move the old server' }).click()

    await page.getByRole('button', { name: 'Start' }).click()

    await expect(page.getByRole('button', { name: 'Pause' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Start' })).toHaveCount(0)
    const control = state.requests.find((request) => {
      return request.method === 'POST' && request.path.endsWith('/control')
    })
    expect(control?.body).toEqual({ action: 'start' })

    await page.getByRole('button', { name: 'Pause' }).click()

    await expect(page.getByRole('button', { name: 'Resume' })).toBeVisible()
  })
})
