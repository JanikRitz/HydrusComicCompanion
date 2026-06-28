# Copilot Instructions

## Project Guidelines
- Prefer MudBlazor dialogs for confirmation flows instead of browser JS confirm prompts, using DialogOptions with top-center positioning when possible.
- For metadata editor dialogs, apply Hydrus and cache updates only when the user clicks Save, and always provide a Cancel option.

## Comic Page Guidelines
- For alternate comic pages, prefer multiple variants per logical page number, using the default variant with manual reader switching, manual grouping in Chapter Placement, and Hydrus metadata as authoritative via a special alternate tag.
- Default promotion UI is not required for alternate comic pages.
- Chapter Placement should use inline text fields for variant labels.
- Ungrouping a row should clear variant metadata.
- Duplicate variant labels should be logged rather than blocked.
- Use a configurable variant namespace in settings; when multiple images share a page, tag the normal image with `variant:default` and require the user to assign a variant label such as `variant:no text` to alternate images during chapter placement.
- Deterministic fallback ordering should use file hash.