# Changelog

## 2.0.0 - 2026-10-22

A full rewrite into a proper desktop app.

### Added
- **Today / Year / History** navigation with a clean, card-based layout.
- **Live current speed** and a **forecast** for today (on track with the finish time, or behind with the shortfall) based on the 10-minute average.
- **Year forecast** from the last 7 days' average, projected to 31 December.
- **Follow year plan**: use today's share of the yearly target as the day target, or apply it with one click.
- **Interactive history chart**: stacked download/upload bars for 14 / 30 / 90 days or the whole year, target and plan lines, hover tooltips, click to select, summary tiles.
- **CSV export** of the full daily history.
- **Mini view**: a draggable, always-on-top widget.
- **Tray icon with a progress ring** for today's target.
- **Notifications** when today's target is reached and when you're falling behind.
- **Light and dark themes** that follow Windows (including the dark title bar), or can be forced.
- **Keyboard shortcuts** and an **About** dialog.
- `/screenshots <folder>` mode with built-in demo data, plus `tools/screenshots.bat`.
- Crash log (`%AppData%\TrafficTarget\error.log`).
- GitHub Actions build and tagged releases.

### Changed
- The code is split into `Core` (settings, formulas, traffic sources) and `UI` (theme, controls, windows).
- "Days left this year" is calculated directly from the Windows date.

## 1.0.0 - 2026-10-21

- First version: Windows app replacing the Excel "Day" and "Year" sheets.
- Daily GB target and yearly TB target with the Windows clock and date.
- Completed traffic from TrafficMonitor's history plus live counting, a built-in monitor, or manual entry.
- Tray icon, start with Windows, keep on top.
