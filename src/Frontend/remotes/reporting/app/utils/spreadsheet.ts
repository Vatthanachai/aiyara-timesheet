/**
 * Builds one-sheet XLSX and CSV files in the browser without a library: the workbook is a minimal
 * Office Open XML package with inline strings, stored in an uncompressed zip.
 */
export type Cell = string | number
/** `table` turns the rows from `headerRow` (0-based) to the last one into an Excel table with
 * filters and banded rows; its header cells must be distinct text. `widths` are in characters. */
export type Sheet = { name: string; rows: Cell[][]; boldRows?: number[]; table?: { headerRow: number; name: string }; widths?: number[] }

const encoder = new TextEncoder()
const escapeXml = (text: string) => text.replace(/[<>&"]/g, char => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' })[char]!)
  // XML 1.0 cannot carry most control characters, even escaped.
  .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '')
const columnName = (index: number) => {
  let name = ''
  for (let n = index + 1; n > 0; n = Math.floor((n - 1) / 26)) name = String.fromCharCode(65 + (n - 1) % 26) + name
  return name
}

const main = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
const rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'

/** The table's cell range; a table needs a row under its header, so an empty one gets a blank row. */
function tableRef({ rows, table }: Sheet) {
  const width = rows[table!.headerRow]!.length
  const last = Math.max(rows.length, table!.headerRow + 2)
  return `A${table!.headerRow + 1}:${columnName(width - 1)}${last}`
}

function sheetXml(sheet: Sheet) {
  const { rows, boldRows = [], widths = [] } = sheet
  const body = rows.map((row, r) => `<row r="${r + 1}">${row.map((value, c) => {
    const ref = `${columnName(c)}${r + 1}`
    const style = boldRows.includes(r) ? ' s="1"' : ''
    // Empty text is left out, so a blank cell stays truly empty.
    if (value === '') return ''
    return typeof value === 'number'
      ? `<c r="${ref}"${style}><v>${value}</v></c>`
      : `<c r="${ref}"${style} t="inlineStr"><is><t xml:space="preserve">${escapeXml(value)}</t></is></c>`
  }).join('')}</row>`).join('')
  const cols = widths.length
    ? `<cols>${widths.map((width, c) => `<col min="${c + 1}" max="${c + 1}" width="${width}" customWidth="1"/>`).join('')}</cols>` : ''
  const tableParts = sheet.table ? '<tableParts count="1"><tablePart r:id="rId1"/></tableParts>' : ''
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="${main}" xmlns:r="${rel}">${cols}<sheetData>${body}</sheetData>${tableParts}</worksheet>`
}

function tableXml(sheet: Sheet) {
  const ref = tableRef(sheet)
  const header = sheet.rows[sheet.table!.headerRow]!.map(String)
  // Table names: a letter first, then letters, digits, or underscores.
  const name = escapeXml(sheet.table!.name.replace(/[^A-Za-z0-9_]/g, '_').replace(/^(?![A-Za-z_])/, 'T'))
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><table xmlns="${main}" id="1" name="${name}" displayName="${name}" ref="${ref}" totalsRowShown="0"><autoFilter ref="${ref}"/><tableColumns count="${header.length}">${header.map((text, c) => `<tableColumn id="${c + 1}" name="${escapeXml(text)}"/>`).join('')}</tableColumns><tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/></table>`
}

function packageFiles(sheet: Sheet): [string, string][] {
  // Excel sheet names: at most 31 characters, none of : \ / ? * [ ].
  const name = escapeXml(sheet.name.replace(/[:\\/?*[\]]/g, ' ').slice(0, 31) || 'Sheet1')
  return [
    ['[Content_Types].xml', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>' +
      (sheet.table ? '<Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>' : '') + '</Types>'],
    ['_rels/.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>`],
    ['xl/workbook.xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="${main}" xmlns:r="${rel}"><sheets><sheet name="${name}" sheetId="1" r:id="rId1"/></sheets></workbook>`],
    ['xl/_rels/workbook.xml.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${rel}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="${rel}/styles" Target="styles.xml"/></Relationships>`],
    ['xl/styles.xml', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="${main}"><fonts count="2"><font/><font><b/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="2"><xf/><xf fontId="1" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>`],
    ['xl/worksheets/sheet1.xml', sheetXml(sheet)],
    ...(sheet.table ? [
      ['xl/worksheets/_rels/sheet1.xml.rels', `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${rel}/table" Target="../tables/table1.xml"/></Relationships>`],
      ['xl/tables/table1.xml', tableXml(sheet)]
    ] as [string, string][] : [])
  ]
}

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})
const crc32 = (bytes: Uint8Array) => {
  let crc = 0xFFFFFFFF
  for (const byte of bytes) crc = crcTable[(crc ^ byte) & 0xFF]! ^ (crc >>> 8)
  return (crc ^ 0xFFFFFFFF) >>> 0
}

/** A zip archive whose entries are stored, not compressed: valid for every unzip tool and Excel. */
export function storedZip(files: [string, Uint8Array][]) {
  const parts: Uint8Array[] = []
  const central: Uint8Array[] = []
  let offset = 0
  for (const [path, data] of files) {
    const name = encoder.encode(path)
    const crc = crc32(data)
    const local = new DataView(new ArrayBuffer(30))
    // Signature, version 2.0, UTF-8 names (bit 11), stored, no date, CRC, sizes, name length.
    local.setUint32(0, 0x04034B50, true); local.setUint16(4, 20, true); local.setUint16(6, 0x0800, true)
    local.setUint32(14, crc, true); local.setUint32(18, data.length, true); local.setUint32(22, data.length, true)
    local.setUint16(26, name.length, true)
    const entry = new DataView(new ArrayBuffer(46))
    entry.setUint32(0, 0x02014B50, true); entry.setUint16(4, 20, true); entry.setUint16(6, 20, true)
    entry.setUint16(8, 0x0800, true); entry.setUint32(16, crc, true); entry.setUint32(20, data.length, true)
    entry.setUint32(24, data.length, true); entry.setUint16(28, name.length, true); entry.setUint32(42, offset, true)
    parts.push(new Uint8Array(local.buffer), name, data)
    central.push(new Uint8Array(entry.buffer), name)
    offset += 30 + name.length + data.length
  }
  const size = central.reduce((sum, part) => sum + part.length, 0)
  const end = new DataView(new ArrayBuffer(22))
  end.setUint32(0, 0x06054B50, true); end.setUint16(8, files.length, true); end.setUint16(10, files.length, true)
  end.setUint32(12, size, true); end.setUint32(16, offset, true)
  return new Blob([...parts, ...central, new Uint8Array(end.buffer)] as BlobPart[], { type: 'application/zip' })
}

export function xlsxBlob(sheet: Sheet) {
  const zip = storedZip(packageFiles(sheet).map(([path, text]) => [path, encoder.encode(text)]))
  return new Blob([zip], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
}

/** CSV with a byte-order mark so Excel reads Thai text as UTF-8. */
export function csvBlob({ rows }: Sheet) {
  const field = (value: Cell) => {
    // Members write the text; a leading = + - @ would otherwise run as a formula in a spreadsheet.
    const text = typeof value === 'string' && /^[=+\-@\t\r]/.test(value) ? `'${value}` : String(value)
    return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
  }
  return new Blob(['﻿', rows.map(row => row.map(field).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' })
}

export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
