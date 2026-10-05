# Aiyara Timesheet Shell

The Shell is a deliberately small Nuxt host. It owns shared navigation, shell styling, locale state,
and route delegation. Business screens live in independently deployed remotes.

Reserved remote routes are `/remote/timesheet`, `/remote/reporting`, and `/remote/administration`.
Set a remote URL through `NUXT_PUBLIC_TIMESHEET_REMOTE_URL`, `NUXT_PUBLIC_REPORTING_REMOTE_URL`,
`NUXT_PUBLIC_ADMINISTRATION_REMOTE_URL`, or `NUXT_PUBLIC_IDENTITY_REMOTE_URL`. The identity/profile
remote is configured for authentication flows but does not have a public navigation link.

Each remote has an independently buildable Nuxt project under `../remotes` and a matching development
URL: Timesheet `http://localhost:3002`, Reporting `http://localhost:3003`, Administration
`http://localhost:3004`, and Identity/Profile `http://localhost:3005`. Port 3005 intentionally avoids
the local Grafana host port (3001). In production, deploy each remote independently and supply the
corresponding Shell environment variable; the Shell only navigates to a remote and does not import its
business code.

The CSS token contract is Tailwind-ready: enable the Tailwind build integration later without changing
components, then map these tokens to the shared Tailwind theme/design system.

## Setup

Make sure to install dependencies:

```bash
# npm
npm install

# pnpm
pnpm install

# yarn
yarn install

# bun
bun install
```

## Development Server

Start the development server on `http://localhost:3000`:

```bash
# npm
npm run dev

# pnpm
pnpm dev

# yarn
yarn dev

# bun
bun run dev
```

## Production

Build the application for production:

```bash
# npm
npm run build

# pnpm
pnpm build

# yarn
yarn build

# bun
bun run build
```

Locally preview production build:

```bash
# npm
npm run preview

# pnpm
pnpm preview

# yarn
yarn preview

# bun
bun run preview
```

Check out the [deployment documentation](https://nuxt.com/docs/getting-started/deployment) for more information.
