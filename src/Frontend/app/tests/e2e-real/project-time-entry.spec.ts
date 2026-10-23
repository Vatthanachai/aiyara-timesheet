import { expect, test } from '@playwright/test'
import { addEmployee, createOrganization } from './support/api'
import { password, unique } from './support/stack'
import { choose, openProfile, openRemote, signIn, signOut, until } from './support/ui'
import { paceSpecs } from './support/pace'

paceSpecs()

// The Tenant Admin adds a project in Administration; a member of the organization signs in, records
// time against that project in Timesheet and sees it, and finds the current device in the profile.
test('a Tenant Admin adds a project and a member records time against it', async ({ page }) => {
  const organization = await createOrganization('Time')
  const employee = await addEmployee(organization)
  const project = `Atlas ${unique()}`
  const task = `Weekly planning ${unique()}`

  // Tenant Admin: Projects and categories -> add a project.
  await signIn(page, organization.adminEmail, password)
  const administration = await openRemote(page, 'Administration')
  await administration.getByRole('tab', { name: 'Projects and categories' }).click()
  const projects = administration.getByRole('region', { name: 'Projects', exact: true })
  await until(async () => {
    await projects.getByLabel('Project name').first().fill(project)
    await expect(projects.getByLabel('Project name').first()).toHaveValue(project)
  })
  await projects.getByRole('button', { name: 'Add item' }).click()
  await expect(projects.locator('.item-list input')).toHaveValue(project)
  await signOut(page)

  // Member: sign in and add an entry for today against the new project.
  await signIn(page, employee.email, employee.password)
  const timesheet = await openRemote(page, 'Timesheet')
  const form = timesheet.locator('.entry-panel')
  await until(async () => {
    await form.getByLabel('Task', { exact: true }).fill(task)
    await expect(form.getByLabel('Task', { exact: true })).toHaveValue(task)
  })
  await choose(form.getByLabel('Project'), project)
  await expect(form.getByLabel('Project')).toHaveValue(project)
  for (const [label, value] of [['Start', '09:00'], ['End', '11:00']] as const) {
    const field = form.getByLabel(label, { exact: true })
    await field.fill(value)
    await field.blur()
    await expect(field).toHaveValue(value)
  }
  await form.getByRole('button', { name: 'Save', exact: true }).click()
  const row = timesheet.locator('tbody tr').filter({ hasText: task })
  await expect(row).toHaveCount(1)
  await expect(row).toContainText(project)
  await expect(timesheet.locator('.month-totals')).toContainText('2 h logged')

  // The profile lists the device just signed in on, marked as this one, with nothing to sign out.
  const profile = await openProfile(page)
  const devices = profile.getByRole('region', { name: 'Signed-in devices' })
  const sessions = devices.getByRole('listitem')
  await expect(sessions).toHaveCount(1)
  await expect(sessions.first().getByText('This device')).toBeVisible()
  await expect(sessions.first().getByRole('button')).toHaveCount(0)
})
