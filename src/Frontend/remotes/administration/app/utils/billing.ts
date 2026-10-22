/** A project's billing details as typed into the form: every field is text so a half-typed value can be judged. */
export type BillingDraft = { client: string; isBillable: boolean; hourlyRate: string; currencyCode: string; budgetHours: string }
export type BillingField = 'client' | 'hourlyRate' | 'currencyCode' | 'budgetHours'
/** The rule a field broke; the page words each one in Thai and English. */
export type BillingProblem = 'rate' | 'currencyFormat' | 'currencyRequired' | 'budget' | 'client'
export type BillingBody = { client: string | null; isBillable: boolean; hourlyRate: number | null; currencyCode: string | null; budgetHours: number | null }

/** A plain decimal with at most two places: no sign, exponent, or grouping. */
const money = /^\d+([.]\d{1,2})?$/

export function validateBilling(draft: BillingDraft): Partial<Record<BillingField, BillingProblem>> {
  const problems: Partial<Record<BillingField, BillingProblem>> = {}
  const rate = draft.hourlyRate.trim()
  const currency = draft.currencyCode.trim()
  const budget = draft.budgetHours.trim()
  if (draft.client.trim().length > 200) problems.client = 'client'
  if (rate && (!money.test(rate) || Number(rate) > 1_000_000)) problems.hourlyRate = 'rate'
  if (currency && !/^[A-Za-z]{3}$/.test(currency)) problems.currencyCode = 'currencyFormat'
  else if (rate && !problems.hourlyRate && !currency) problems.currencyCode = 'currencyRequired'
  if (budget && (!money.test(budget) || Number(budget) <= 0 || Number(budget) > 1_000_000)) problems.budgetHours = 'budget'
  return problems
}

/** The `PUT projects/{id}/billing` body: empty text clears a field. */
export function billingBody(draft: BillingDraft): BillingBody {
  const rate = draft.hourlyRate.trim()
  const budget = draft.budgetHours.trim()
  return {
    client: draft.client.trim() || null, isBillable: draft.isBillable,
    hourlyRate: rate ? Number(rate) : null, currencyCode: draft.currencyCode.trim().toUpperCase() || null,
    budgetHours: budget ? Number(budget) : null
  }
}
