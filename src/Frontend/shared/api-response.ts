export type ApiEnvelope<T> = {
  statusCode: number
  statusMessage: string
  result: T
}

export type ApiError = {
  statusCode: number
  statusMessage: string
  errorCode: string
  errors?: Record<string, string[]>
  traceId?: string
}

export type Locale = 'th' | 'en'

export function unwrapResponse<T>(response: ApiEnvelope<T>): T {
  return response.result
}

export function safeError(error: unknown, locale: Locale): string {
  const candidate = error as { data?: Partial<ApiError>; status?: number } | null
  const payload = candidate?.data
  const status = payload?.statusCode ?? candidate?.status
  const code = payload?.errorCode
  const thai = locale === 'th'
  if (status === 401) return thai ? 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง' : 'Session expired. Please sign in again.'
  if (status === 403) return thai ? 'คุณไม่มีสิทธิ์ทำรายการนี้' : 'You do not have permission for this action.'
  if (status === 404) return thai ? 'ไม่พบข้อมูลที่ต้องการ' : 'The requested item was not found.'
  if (status === 409) return thai ? 'ข้อมูลเปลี่ยนแปลงแล้ว กรุณาลองใหม่' : 'The data changed. Please try again.'
  if (status === 429) return thai ? 'ส่งคำขอถี่เกินไป กรุณารอสักครู่' : 'Too many requests. Please wait a moment.'
  if (code === 'VALIDATION_FAILED' || status === 400)
    return thai ? 'กรุณาตรวจสอบข้อมูลที่กรอก' : 'Please check the entered information.'
  const trace = payload?.traceId ? ` (${thai ? 'รหัสติดตาม' : 'Trace ID'}: ${payload.traceId})` : ''
  return (thai ? 'ระบบขัดข้อง กรุณาลองใหม่' : 'Something went wrong. Please try again.') + trace
}

export function fieldErrors(error: unknown, locale: Locale): Record<string, string[]> {
  const fields = (error as { data?: ApiError } | null)?.data?.errors ?? {}
  return Object.fromEntries(Object.entries(fields).map(([field, codes]) => [field,
    codes.map(code => {
      if (code === 'REQUIRED') return locale === 'th' ? 'จำเป็นต้องกรอก' : 'Required'
      if (code === 'INVALID_DATE') return locale === 'th' ? 'วันที่ไม่ถูกต้อง' : 'Invalid date'
      return locale === 'th' ? 'ข้อมูลไม่ถูกต้อง' : 'Invalid value'
    })
  ]))
}
