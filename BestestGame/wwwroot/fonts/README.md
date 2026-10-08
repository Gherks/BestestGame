# Local font assets

Both families are distributed under the bundled SIL Open Font License files.
The application serves these files locally; it makes no external font requests.

Source: https://github.com/google/fonts
Pinned source commit: `5e8a3ba899557829a76cfdac30fa512bda91d7ca`

The upstream TrueType files were losslessly packaged as WOFF2 using the locally
available FontTools encoder. No glyphs or variation axes were subsetted; system
fallbacks cover characters outside each family's glyph set. Libre Baskerville is
variable from regular to bold; Barlow Condensed is two static weights.

| Asset | Upstream path | WOFF2 SHA-256 |
| --- | --- | --- |
| `libre-baskerville-variable.woff2` | `ofl/librebaskerville/LibreBaskerville[wght].ttf` | `1ce91cc87fd7602c7d4d1a925af15aaefbb85acd379dbee9b34a82e653669320` |
| `libre-baskerville-italic-variable.woff2` | `ofl/librebaskerville/LibreBaskerville-Italic[wght].ttf` | `e9349fa52e63a21f705f7935a1f7c1a2a77f1e97e0546e245f221134e94f09b9` |
| `barlow-condensed-semibold.woff2` | `ofl/barlowcondensed/BarlowCondensed-SemiBold.ttf` | `fa9dce30a032594dc73eabdee132dd851120e606fd35452e3cb870006bd54475` |
| `barlow-condensed-bold.woff2` | `ofl/barlowcondensed/BarlowCondensed-Bold.ttf` | `2e41b146e59a32f84f32301aa7891e0e07323d08d4d6991af06b0ee347d943b0` |
