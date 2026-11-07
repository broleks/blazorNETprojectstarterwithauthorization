# Visual style prompt

This file is the single source of truth for how the user interface should look and feel.
Point to it whenever you (or an AI assistant) work on the frontend, for example:

> Follow `design/STYLE-PROMPT.md` and the tokens in `src/Starter.Ui/wwwroot/theme.css`.

Write the style as a prompt: concrete, short sentences an assistant or a designer can follow.
Replace the placeholder text in each section. Keep `theme.css` in line with what is written here —
change the tokens there first, then individual components.

---

## Style prompt

<!-- Paste or write the agreed style prompt here. Everything below is a starting point. -->

_Not defined yet._ Until it is, the starter uses a calm, neutral look: light grey background, white cards,
one blue accent colour, system fonts, generous spacing and rounded corners.

## Brand and tone

- Personality (e.g. calm, precise, friendly):
- Words to use / avoid in the UI:

## Colour

- Primary / accent:
- Neutrals (background, surface, borders, text):
- Status colours (success, error, warning):
- Dark mode: follows the operating system (`prefers-color-scheme`); describe any differences here.

## Typography

- Font families:
- Sizes and weights for headings, body, captions:

## Layout and spacing

- Desktop: content on the left, navigation on the right.
- Small screens: top bar with a menu button; navigation opens as a panel.
- Spacing scale and maximum content width:

## Components

- Buttons (primary, secondary, danger):
- Forms (labels above fields, error text below, hint text):
- Cards, lists, empty states, loading states, confirmation dialogs, toasts:

## Accessibility rules (do not remove)

- Every input has a visible label; errors are announced (`role="alert"`) and linked to the field.
- Visible focus ring on every interactive element; full keyboard navigation.
- Colour contrast at least WCAG 2.2 AA (4.5:1 for text).
- Touch targets at least 44×44 px; respect `prefers-reduced-motion`.
- Never show technical details (tokens, stack traces, HTTP codes) to users.

## Do / don't

- Do:
- Don't:
