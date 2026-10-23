import { expect, test } from '@playwright/test'
import { createOrganization } from './support/api'
import { invitationSubject, waitForMail } from './support/mail'
import { password, unique } from './support/stack'
import { openRemote, signIn, until } from './support/ui'
import { paceSpecs } from './support/pace'

paceSpecs()

// A Tenant Admin invites two people at once; both appear as pending invitations and their emails go
// out, and the organization's audit log lists both invitations with the admin as the actor.
test('a Tenant Admin bulk-invites two members and the audit log shows both invitations', async ({ page }) => {
  const organization = await createOrganization('Bulk')
  const invited = [`bulk-one-${unique()}@example.test`, `bulk-two-${unique()}@example.test`]

  await signIn(page, organization.adminEmail, password)
  const administration = await openRemote(page, 'Administration')
  await administration.getByRole('tab', { name: /^People/ }).click()
  await administration.getByRole('button', { name: 'Invite several' }).click()
  const input = administration.getByLabel('Paste emails or CSV data')
  await until(async () => {
    await input.fill(invited.join('\n'))
    await expect(input).toHaveValue(invited.join('\n'))
    await expect(administration.getByRole('button', { name: 'Send 2 invitations' })).toBeEnabled()
  })
  await administration.getByRole('button', { name: 'Send 2 invitations' }).click()
  await expect(administration.getByText('2 invited')).toBeVisible()
  await expect(administration.getByRole('table', { name: 'Invitation results' }).locator('tbody tr')).toHaveCount(2)

  // Both are waiting in the pending invitations, and both emails were delivered.
  const pending = administration.getByRole('region', { name: 'Pending invitations' })
  for (const email of invited) await expect(pending).toContainText(email)
  for (const email of invited) await waitForMail(email, invitationSubject)

  // The audit log: one "Member invited" entry per address, done by the admin.
  await administration.getByRole('tab', { name: 'Audit log' }).click()
  const entries = administration.getByRole('list', { name: 'Audit entries' }).locator(':scope > li')
  for (const email of invited) {
    const entry = entries.filter({ hasText: email })
    await expect(entry).toHaveCount(1)
    await expect(entry).toContainText('Member invited')
    await expect(entry).toContainText(`by ${organization.adminEmail}`)
  }
})
