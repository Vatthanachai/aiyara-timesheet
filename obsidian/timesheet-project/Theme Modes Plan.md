# Theme Modes Plan

> Drafted 22 October 2026 on `develop` at `4edc179`. Branch `feature/theme-modes`. Adds light, dark, and system themes to the Shell and the four remotes, and gives the interface depth. Follows [[Home Page Plan]] and the time-card language of the sign-in and signed-out pages.

## Implementation Progress

- **Phases A–C** implemented on `feature/theme-modes` (22 October 2026). Validation is recorded in [[Project Status]].

## Decisions

1. Frontend only. No API change.
2. The header has a theme switch beside the language switch: Light, Dark, and System, drawn as a sun, a moon, and a screen. It is a radio group like the language switch (arrow keys move the choice; only the chosen option is a tab stop).
3. The choice is remembered in this browser (`localStorage` `aiyara.theme`). With no choice, or with System, the theme follows the operating system and changes when it does. A script in the page head applies the remembered theme before the first paint, so a dark page never flashes light.
4. **Message contract:** the Shell sends the resolved theme (`light` or `dark`, never `system`) to every remote as `theme` in the `aiyara.session` message, beside `locale` and the date and time formats, and resends it when it changes. Remotes read it with `readSessionTheme` and apply it with `applyTheme` from `shared/theme.ts`; a message without `theme` means light.
5. Every app sets `data-theme` on `<html>`; `shared/tokens.css` defines both themes. No component keeps a colour of its own: every hard-coded colour in the Shell and the remotes became a token (surface, muted surface, field, ink, soft ink, faint, strong border, brand ink, soft and line tints, danger, success, warning, leave).

## Design Direction

Paper on a desk. The canvas is the desk; panels are sheets of paper lifted by a contact shadow and a soft spread (`--elev-1`), and what floats above the page — the time card, the profile menu — sits higher (`--elev-2`). Fields sink below the paper (`--color-field`, `--inset-field`). The primary button presses: a light top edge and a coloured shadow at rest, one pixel down with the shadow gone while pressed, and flat when it cannot be pressed. The header is sticky and translucent on wider screens, so the page slides under it.

The dark theme is the same desk after hours: a deep navy desk (`#0d1524`) rather than black, slate paper (`#162133`), and a lamp-lit blue (`#7aa5ff`) that carries dark text when it fills a control. Shadows are cast in near-black and fall harder, with a faint top edge of light on each sheet.

## Phases

- [x] A1. `shared/tokens.css` light and dark tokens, depth tokens; `shared/theme.ts`.
- [x] A2. Shell `useTheme`, the head script, `ThemeSwitch` in the header; the theme in `aiyara.session`.
- [x] B1. Every colour literal in the Shell and the remotes replaced with a token.
- [x] B2. `shared/depth.css` in each remote (desk, sunken fields, pressing primary button) and each remote's panels lifted.
- [x] C1. Phone header: the profile stays beside the brand and the two switches wrap under it; the header scrolls away on phones.
- [x] C2. Tests: the switch (system, follows the system, remembered, arrow keys) and Timesheet taking the Shell's theme.
