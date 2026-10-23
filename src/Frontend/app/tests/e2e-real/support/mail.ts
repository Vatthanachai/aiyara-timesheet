import { sleep, stack } from './stack'

type MailDevMessage = { subject: string; text?: string; to?: { address: string }[] }

/** The newest MailDev message to `to` whose subject is `subject`, polled until it arrives. */
export async function waitForMail(to: string, subject: string, timeoutMs = 90_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const response = await fetch(`${stack.mailUrl}/email`)
    if (response.ok) {
      const messages = await response.json() as MailDevMessage[]
      const found = messages.filter(message => message.subject === subject
        && message.to?.some(recipient => recipient.address.toLowerCase() === to.toLowerCase())).at(-1)
      if (found) return found
    }
    await sleep(500)
  }
  throw new Error(`The email "${subject}" to ${to} did not arrive within ${timeoutMs / 1000} s.`)
}

/** The one-time code in an activation or invitation email ("... code: ABC123"). */
export async function waitForMailCode(to: string, subject: string, timeoutMs?: number) {
  const message = await waitForMail(to, subject, timeoutMs)
  const code = /code:\s*(\S+)/i.exec(message.text ?? '')?.[1]
  if (!code) throw new Error(`The email "${subject}" to ${to} has no code.`)
  return code
}

export const activationSubject = 'Activate your Aiyara Timesheet account'
export const invitationSubject = 'Your Aiyara Timesheet invitation'
