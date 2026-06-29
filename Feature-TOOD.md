
## Issues (?)

- [ ] Alternates of the first / cover page not working properly
- [ ] when moving images in ChapterPlacementStep to the top two chapters are made (should replace?)
- [ ] larger icon for opening the Alternates

## Metadata

- [X] store additional data as notes in page:1 / meta:cover page images
- [X] what about OCR data, I already have a tool + format that stores the data
  (should the OCR data be cached for search?)
- [ ] store favorites (maybe also as info on the cover page in Hydrus using the rating system already provided by Hydrus which would mean we need this configured in settings)

## Comic View

- [ ] open search when clicking on tags

## Search

search by

- [X] Title
- [X] Tags
- [X] OCR (?) / full text search where available

filter by
- [X] length (pages, chapters, ...)
- [X] tags


## Import

Add Full Title + comment to import metadata step

- [X] calibre
  - read metadata, like creator + series/volume
  - additional info
  - how do we access the data, CLI interface or directly reading the DB?
- [X] other tag services, show thumbnails of possible ones (using cover or page 1) or even do a queue from them.
  automatically add creator + series, or even better a list of configured namespaces to the import

## UI

- [X] show Hydrus Tags per page (possibly even allow editing or opening in Hydrus)
- [X] show more info on Comic card (favorite, series, creator, ...)
- [X] Make images and thumbnails even before import to Hydrus available (?) right now it's just base64 encoded images when not yet in Hydrus

## Other

already mentioned in the plan.md is the export as CBZ for reading on device

## OCR

- [X] add a "scan OCR' button that checks for sidecar files and ingests them if there are
- [X] writing the data to cache + Hydrus note (configured name) . This only adds a plain text variant keeping the full OCR in the sidecar 
- [ ] should ignore files with OCR notes already (?)
- [ ] enable option in Reader to overlay the OCR data (will need to read the sidecar file for full OCR)
- [ ] enable full text search, optionally with highlight of which page(s)

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