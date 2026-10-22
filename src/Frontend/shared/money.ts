import type { Locale } from './api-response'

/** An amount in its own currency ("฿1,200.00", "1,200.00 XYZ" for a code the browser does not know). */
export function formatMoney(amount: number, currencyCode: string | null | undefined, locale: Locale) {
  const tag = locale === 'th' ? 'th-TH' : 'en-GB'
  try {
    return new Intl.NumberFormat(tag, { style: 'currency', currency: currencyCode || undefined, currencyDisplay: 'code' }).format(amount)
  } catch {
    const number = new Intl.NumberFormat(tag, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)
    return currencyCode ? `${number} ${currencyCode}` : number
  }
}

/** Hours with at most two decimals ("7.5"), from hours. */
export function formatHours(hours: number, locale: Locale) {
  return new Intl.NumberFormat(locale === 'th' ? 'th-TH' : 'en-GB', { maximumFractionDigits: 2 }).format(hours)
}

/** Hours from minutes ("7.5"). */
export const formatMinutes = (minutes: number, locale: Locale) => formatHours(minutes / 60, locale)
