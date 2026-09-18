# Chrono Save 1.1.0

Not a store page. This is the text to post as a Workshop comment when 1.1.0 goes up,
because the in-game uploader hardcodes its own change note and cannot write one.

---

**Chrono Save 1.1.0**

The first update since August 2025. Every open issue is fixed.

Fixed

- Chrono Save could write a truncated save file. It saved while you were still on the
  starting-site screen, where part of the game state does not exist yet, and RimWorld does
  not report that as an error: the half-written file is committed and sits in your save list
  looking normal. If you have a suspiciously small Chronosave file, that is what it is.
- Chronosaves were numbered from a counter stored inside your save, so two colonies shared
  one rotation and could overwrite each other. The slot is now worked out from the files on
  disk and kept separate per colony. Existing files are adopted rather than abandoned.
- Chrono Save wrote saves in Commitment mode, which is not something Commitment mode should
  allow. It no longer does.
- Saving no longer interrupts you mid-action.
- The interval could not be typed into, only dragged, and the slider's tooltip could not be
  reached.
- The message saying a chronosave had happened appeared when the save was queued rather than
  when it finished, so it appeared even when the save failed.

Changed, and worth reading before you update

- Chrono Save no longer requires Harmony. It had one patch, that patch did nothing, and the
  dependency went with it. If Harmony is in your mod list for other reasons it stays; if it
  was there only for this mod, it is no longer needed.
- Renaming a colony is how Chrono Save tells colonies apart. Vanilla 1.6 has no rename button
  of its own outside Commitment mode, so the mod opens RimWorld's own naming dialog from its
  settings.

The mod no longer ships its source files to subscribers.
