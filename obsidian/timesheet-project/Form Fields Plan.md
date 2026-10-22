# Form Fields Plan

> Drafted 22 October 2026 on `develop` at `ad1d3d9`. Branch `feature/form-fields`. Replaces every dropdown, date input, and time input in the Shell and the four remotes with three shared fields. Follows [[Theme Modes Plan]] (the list floats at `--elev-2`) and [[Date And Time Format Plan]].

## Implementation Progress

- **Phases A–C** implemented on `feature/form-fields` (22 October 2026). Validation is recorded in [[Project Status]].

## Decisions

1. Frontend only. No API change: each field's model is the same value the native control gave (an option's value, `YYYY-MM-DD`, `HH:mm`).
2. **Every dropdown is an autocomplete** (`shared/components/AutoComplete.vue`, the ARIA combobox pattern). Typing filters the list by label or hint and marks the matched part; arrow keys, Home, and End move through it; Enter picks; Escape puts back the last choice; leaving the field keeps a choice only when the text names one, so a field never holds text that is not an option. An optional choice can be cleared by emptying the text. Choices that belonged to an `<optgroup>` carry its name as a hint (Reports' "Report on": Groups, People).
3. **Date fields are always dd/MM/yyyy** with a Gregorian year, whatever the organization's date format (`shared/components/DateField.vue`, moved from Administration). This supersedes the earlier rule that a date was typed in the organization's format; dates shown as text elsewhere still follow it. Dashes, dots, and one-digit days are accepted and tidied on leaving. `min`/`max` refuse dates outside the range with a message naming it. The calendar button is still the browser's own picker.
4. **Time fields are always 24-hour HH:mm** (`shared/components/TimeField.vue`, built on the autocomplete), whatever the organization's time format. They suggest every half hour (every quarter hour for Administration's working hours and break), narrowed by typed digits, and accept 9, 930, 9.30, or 09:30. A complete time counts while it is typed, so the entry's duration follows the typing.
5. Each app provides its locale once (`provide(fieldLocaleKey, locale)` from `shared/i18n.ts`), so the fields' own words (No matches, how to type a date, the calendar button) follow the language without every caller passing it.
6. Month pickers (`type="month"`) are unchanged.

## Phases

- [x] A1. `AutoComplete`, `TimeField`, `DateField` in `shared/components`; `FIELD_FORMATS` and `parseTime` in `shared/date-format.ts`; `fieldLocaleKey` and `useFieldLocale` in `shared/i18n.ts`.
- [x] B1. Timesheet: the entry form (start, end, date, project, category), leave (date, kind), and the saved task's status.
- [x] B2. Reports: the report request (report, week, month, year, from, to, report on), the history filters, report formats, automatic reports (report, report on, time zone, time), and the member timesheets' group filter.
- [x] B3. Administration: date and time format, invitation role and group, each member's group, working hours and the break, holiday dates. System admin: the audit log's action, from, and to.
- [x] C1. Tests: `tests/e2e/fields.ts` (`choose`, `options`, `shownDate`) drives the fields as a person does; a new Timesheet test covers filtering, the marked match, Enter, Escape, arrow keys, 24-hour suggestions, loosely typed times, and the dd/MM/yyyy date with its range.
