/**
 * Parsing and checking for "Invite several": pasted text or a CSV file becomes rows of
 * email, role, and group, each checked in the browser before anything is sent.
 */

export const MAX_BULK_ROWS = 100
export type BulkRole = 'Employee' | 'TenantAdmin'
/** A problem found in a row before sending. */
export type RowProblem = 'invalidEmail' | 'duplicate' | 'invalidRole' | 'unknownGroup'
export type ParsedRow = {
  /** 1-based position among the data rows. */
  line: number
  email: string
  /** The role as typed; empty means Employee. */
  roleText: string
  /** The group name as typed. */
  groupText: string
  role: BulkRole | null
  /** The matched group's id, or null for no group or an unknown name. */
  groupId: string | null
  problem: RowProblem | null
}
export type GroupRef = { id: string; name: string }

/** Splits delimited text into rows of cells: quoted fields may hold the delimiter, a doubled quote, and line breaks. */
export function splitDelimited(input: string, delimiter: string): string[][] {
  const text = input.replace(/^﻿/, '')
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let quoted = false
  let cellStarted = false
  const endCell = () => { row.push(cell.trim()); cell = ''; cellStarted = false }
  const endRow = () => { endCell(); if (row.some(value => value !== '')) rows.push(row); row = [] }
  for (let i = 0; i < text.length; i++) {
    const char = text[i]!
    if (quoted) {
      if (char === '"') {
        if (text[i + 1] === '"') { cell += '"'; i++ } else quoted = false
      } else cell += char
    } else if (char === '"' && !cellStarted) { quoted = true; cellStarted = true }
    else if (char === delimiter) endCell()
    else if (char === '\r') { if (text[i + 1] === '\n') i++; endRow() }
    else if (char === '\n') endRow()
    else { cell += char; cellStarted = cellStarted || char.trim() !== '' }
  }
  endRow()
  return rows
}

/** The delimiter used by the first non-empty line: tab, then semicolon, then comma (the most frequent outside quotes). */
export function detectDelimiter(input: string): string {
  const text = input.replace(/^﻿/, '')
  const first = text.split(/\r\n|\n|\r/).find(line => line.trim() !== '') ?? ''
  const counts: Record<string, number> = { '\t': 0, ';': 0, ',': 0 }
  let quoted = false
  for (const char of first) {
    if (char === '"') quoted = !quoted
    else if (!quoted && char in counts) counts[char]!++
  }
  return (['\t', ';', ','] as const).reduce((best, next) => counts[next]! > counts[best]! ? next : best, ',' as string)
}

const EMAIL = /^[^\s@,;"<>]+@[^\s@,;"<>]+\.[^\s@,;"<>]+$/
export const isEmail = (value: string) => value.length <= 320 && EMAIL.test(value)

const ROLE_NAMES: Record<string, BulkRole> = {
  employee: 'Employee', member: 'Employee', staff: 'Employee', 'พนักงาน': 'Employee',
  tenantadmin: 'TenantAdmin', 'tenant admin': 'TenantAdmin', admin: 'TenantAdmin', administrator: 'TenantAdmin',
  'organization admin': 'TenantAdmin', 'organisation admin': 'TenantAdmin', 'ผู้ดูแลองค์กร': 'TenantAdmin'
}
/** A role as typed: empty is Employee; anything unrecognized is null. */
export function parseRole(text: string): BulkRole | null {
  const key = text.trim().toLowerCase().replace(/[_-]+/g, ' ').replace(/\s+/g, ' ')
  if (key === '') return 'Employee'
  return ROLE_NAMES[key] ?? ROLE_NAMES[key.replace(/ /g, '')] ?? null
}

const HEADER_NAMES: Record<string, 'email' | 'role' | 'group'> = {
  email: 'email', 'e-mail': 'email', 'e mail': 'email', mail: 'email', 'email address': 'email', 'อีเมล': 'email',
  role: 'role', 'บทบาท': 'role', group: 'group', 'กลุ่ม': 'group'
}

/**
 * Parses pasted text or a CSV file: columns email, role, group in that order, or in the order a header row names them
 * (extra columns are ignored). A line holding only an address is an Employee with no group.
 * `groups` are the organization's groups, matched by name ignoring case.
 */
export function parseInviteRows(input: string, groups: GroupRef[]): ParsedRow[] {
  const cleaned = input.replace(/^﻿/, '')
  if (cleaned.trim() === '') return []
  const cells = splitDelimited(cleaned, detectDelimiter(cleaned))
  let columns = { email: 0, role: 1, group: 2 } as Record<'email' | 'role' | 'group', number>
  const headerKinds = (cells[0] ?? []).map(value => HEADER_NAMES[value.trim().toLowerCase()])
  // A header names an email column; an address in the first cell means the first row is data.
  if (headerKinds.includes('email') && !(cells[0] ?? []).some(isEmail)) {
    columns = { email: headerKinds.indexOf('email'), role: headerKinds.indexOf('role'), group: headerKinds.indexOf('group') }
    cells.shift()
  }
  const at = (row: string[], index: number) => index < 0 ? '' : (row[index] ?? '').trim()
  const byName = new Map(groups.map(group => [group.name.trim().toLowerCase(), group.id]))
  const seen = new Set<string>()
  return cells.map((row, index) => {
    const email = at(row, columns.email)
    const roleText = at(row, columns.role)
    const groupText = at(row, columns.group)
    const role = parseRole(roleText)
    const groupId = groupText ? byName.get(groupText.toLowerCase()) ?? null : null
    const key = email.toLowerCase()
    let problem: RowProblem | null = null
    if (!isEmail(email)) problem = 'invalidEmail'
    else if (seen.has(key)) problem = 'duplicate'
    else if (!role) problem = 'invalidRole'
    else if (groupText && !groupId) problem = 'unknownGroup'
    if (isEmail(email)) seen.add(key)
    return { line: index + 1, email, roleText, groupText, role, groupId, problem }
  })
}

/** One CSV cell, quoted when needed; a leading =, +, -, or @ is neutralized so a spreadsheet never runs it as a formula. */
export function csvCell(value: string): string {
  const safe = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value
  return /[",;\n\r\t]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe
}

export const toCsv = (header: string[], rows: string[][]) =>
  [header, ...rows].map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n'

/** The request body for the rows that passed the checks. */
export const bulkBody = (rows: ParsedRow[]) => ({
  rows: rows.map(row => ({ email: row.email, role: row.role!, ...(row.groupId ? { groupId: row.groupId } : {}) }))
})
