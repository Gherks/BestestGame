# Local font assets

Both families are distributed under the bundled SIL Open Font License files.
The application serves these files locally; it makes no external font requests.

Source: https://github.com/google/fonts
Pinned source commit: `5e8a3ba899557829a76cfdac30fa512bda91d7ca`

The upstream variable TrueType files were losslessly packaged as WOFF2 using
the locally available FontTools encoder. No glyphs or variation axes were
subsetted; system fallbacks cover characters outside each family's glyph set.

| Asset | Upstream path | WOFF2 SHA-256 |
| --- | --- | --- |
| `bricolage-grotesque-variable.woff2` | `ofl/bricolagegrotesque/BricolageGrotesque[opsz,wdth,wght].ttf` | `7e809b60a9dd58f4ae2f47e6dd35223bbf96499c3932b9c28481e3736bb4ca80` |
| `dm-sans-variable.woff2` | `ofl/dmsans/DMSans[opsz,wght].ttf` | `7560859aba3530285d1b50d9e2afa2e82e5c3b83761a478f54e2333cddff299d` |
