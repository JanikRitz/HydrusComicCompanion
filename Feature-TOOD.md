
## Metadata

store additional data as notes in page:1 / meta:cover page images

what about OCR data, I already have a tool + format that stores the data

(should the OCR data be cached for search?)

store favorites (maybe also as info on the cover page in Hydrus using the rating system already provided by Hydrus which would mean we need this configured in settings)

## Search

search by
- Title
- Tags
- OCR (?) / full text search where available

filter by
- length (pages, chapters, ...)
- tags


## Import

- calibre
  - read metadata, like creator + series/volume
  - additional info
  - how do we access the data, CLI interface or directly reading the DB?

## UI

show Hydrus Tags per page (possibly even allow editing or opening in Hydrus)

show more info on Comic card (favorite, series, creator, ...)

## Other

already mentioned in the plan.md is the export as CBZ for reading on device


## OCR data format

file is named the same as the original file + extension is replaced with `.ocr.json`

rough schema outline:

```json
{
"schemaVersion":1,
"source":"<filename>",
"createdUtc":"<UTC-Time>",
"updatedUtc":"<UTC-Time>",
"defaultSpeakerColor":"<html color>",
"speakers":{},
"segments": [{"uid":"<uid>", "corners":[{"x":142,"y":0},{"x":1043,"y":540},{"x":996,"y":618}], "text":"<OCR text>"}, ... ],
"blocks": [{"uid":"<uid>","order":1,"segmentIds":["<uid>"]}, ... ]
}
```