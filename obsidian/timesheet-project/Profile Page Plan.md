# Profile Page Plan

> Drafted 21 October 2026 from a code audit of `develop` at `442346d`. Branch `feature/profile-page`. Covers the profile settings page, the identity-profile remote (`src/Frontend/remotes/identity-profile`), opened from the Shell's profile menu. Follows the other page plans ([[Home Page Plan]], [[Administration Page Plan]]) and their tokens and type.

## Implementation Progress

- **Phases A–B** implemented on `feature/profile-page` (21 October 2026). Validation is recorded in [[Project Status]].

## Audit: What Exists On `develop`

| Area | Today | Problem |
|---|---|---|
| Heading | "AIYARA ACCOUNT" eyebrow, "Profile settings", a subtitle | Eyebrow chrome; the subtitle says nothing the page does not show |
| Photo | A card with the photo, name, email, "Change photo", "Remove photo", a file name and "Save photo" after choosing, and "or drag an image here" | Fine on its own, but its errors and confirmations appear at the bottom of the other card |
| Details | First name, last name, job title, and the email shown again as text with an "Account email" badge | The email appears three times; nothing shows what changes will look like or whether there are unsaved edits; Save is always enabled |
| Look | Two identical cards with the same shadow; buttons lift on hover | The card kit; motion on every button |

## Decisions

1. Frontend only, in the identity-profile remote. No API change. Uploading, removing, and saving work as before, and the Shell is still told after each so its header follows.
2. The page opens with **your card**: the photo (or initials), your name, and job title as colleagues see them, drawn from what is being typed, so a change shows before it is saved. The card is also where a photo is dropped or chosen.
3. Beside the card, a line shows how you appear in the Shell's header (the avatar and name chip), from the same live values.
4. The email appears once, as the account's sign-in email, read-only, with the note that it cannot be changed (no Identity route changes an email, for members or admins).
5. Photo messages appear under the photo; details messages beside Save. Each keeps its own status region.
6. Save is enabled only when the details differ from what was last saved, and "Unsaved changes" says so; Cancel puts back the saved values.
7. A photo that has been chosen but not saved is marked as a preview on the card, with Save photo and Cancel next to it.

## Design Direction

The card is the memorable element: you, as your organization sees you, set large in the display face. Everything else is a quiet form.

- **Colour**: shared tokens. Ledger blue `#155EEF` (primary action, initials), working-hours tint `#DCE6FD` (the initials disc and the preview mark), ink `#172033`, muted `#5D687A`, error `#B42318`, success `#067647`.
- **Type**: Bai Jamjuree for the name on the card, initials, and headings; the Inter/system stack for fields and help.
- **Layout**: left-aligned. Wide: the card on the left (about 20rem), the details form on the right. Phones: the card on top, then the form. One border per area, no shadows.
- **Initials**: `shared/initials.ts` skips a Thai leading vowel (เ แ โ ใ ไ), so ใจดี gives จ; the Shell header uses the same helper.
- **Motion**: none on load; no hover lifts. The card updates as you type.

```
┌──────────────────────┐  Personal details
│       ( SJ )         │  First name [Somchai     ]  Last name [Jaidee   ]
│   Somchai Jaidee     │  Job title  [Project manager                    ]
│   Project manager    │  Sign-in email  somchai@acme.test
│ [Change photo] Remove│  (you sign in with it; it cannot be changed)
│ JPEG, PNG, WebP ≤5 MB│  ───────────────────────────────────────────────
└──────────────────────┘  Unsaved changes            [Cancel] [Save changes]
In the header: (SJ) Somchai Jaidee ▾
```

## Phase A — Card And Form

- [x] A1. Your card from live values, the photo actions on it, drag and drop onto it, the unsaved-photo preview mark with Save photo and Cancel.
- [x] A2. The header preview line.
- [x] A3. Details form with the read-only sign-in email, the unsaved-changes state, Cancel, and Save enabled only when something changed.
- [x] A4. Messages next to the action they answer.

## Phase B — Quality And Docs

- [x] B1. Bai Jamjuree in the remote; keyboard focus; reduced motion; 1280 / 768 / 375 / 320 px with no sideways scroll; screenshots in Thai and English.
- [x] B2. Playwright: the existing profile test follows the new page; new checks for the live card, the unsaved state and Cancel, and Save enabled only after a change.
- [x] B3. identity-profile `nuxt build`; Docker Compose `identity-profile-remote` rebuilt and healthy. Docs: [[Project Status]], [[Timesheet Project Index]].

## Out Of Scope

- Changing the email or password from this page, and new profile fields.

## Related

- [[Home Page Plan]]
- [[Administration Page Plan]]
- [[Project Status]]
