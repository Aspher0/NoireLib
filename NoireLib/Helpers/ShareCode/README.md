# Helper Documentation : ShareCodeHelper

You are reading the documentation for the `ShareCodeHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [Encoding](#encoding)
- [Decoding](#decoding)
- [Kinds](#kinds)
- [Limits](#limits)
- [Format & Wire Layout](#format--wire-layout)
- [Security Notes](#security-notes)

---

## Overview

`ShareCodeHelper` is a static helper in the `NoireLib.Helpers` namespace that turns anything serializable into a single
pasteable token, and reads one back telling you exactly why it could not when it could not.

It is a plain data helper. It touches no ImGui and depends on nothing in `NoireLib.UI`, so it works just as readily from
a command handler, a background task or a module as it does from a window.

A code is:

- **Versioned** - the marker carries the format version, so a code from a newer build reads as "update the plugin"
  rather than as damage.
- **Compressed** - deflated when that actually helps, and left raw when it does not.
- **Checksummed** - a CRC-32 over the payload as it will be parsed, so a truncated or re-wrapped paste is caught.
- **Tagged with a kind** - a code meant for one thing is refused by another instead of being half-applied.
- **Bounded** - decoding runs under published ceilings that are part of the format, not a preference.

---

## Encoding

```csharp
using NoireLib.Helpers;

var code = ShareCodeHelper.Encode("myplugin.preset", preset);
// NOIRE1-AQTf3k1tZXN...
```

The result is one token with no spaces, no padding and no `+` or `/`, so it survives being pasted into a chat box, a
Discord message or a forum post.

Encoding throws rather than producing a code no conformant reader would accept:

- `ArgumentException` when the kind is blank or over `MaxKindBytes`.
- `InvalidOperationException` when the payload or the finished code exceeds [the limits](#limits). Producing an
  oversized code would only move the failure to whoever pasted it.

---

## Decoding

```csharp
var result = ShareCodeHelper.Decode<PresetDto>(pastedText, "myplugin.preset");

if (result.Success)
    ShowPreview(result.Value);
else
    ShowError(result.Message);   // already phrased for a user
```

Decoding returns a `ShareCodeResult<T>` rather than throwing. The input is authored by a stranger and pasted by hand, so
a bad paste is an ordinary thing that happens, not an exceptional one, and it deserves a message in the window rather
than a stack trace in the log.

`result.Error` is a `ShareCodeError`, and every value is a reason a user can be told:

| Error | What happened |
|---|---|
| `Empty` | Nothing was passed, or only whitespace. |
| `NotAShareCode` | The text is not a share code at all. |
| `WrongVersion` | A share code in a format version this build cannot read. |
| `Malformed` | Damaged: truncated, re-wrapped, or partially copied. |
| `ChecksumMismatch` | It parsed, but its contents do not match its own checksum. |
| `TooLarge` | It asks for more than the limits allow. |
| `WrongKind` | Valid, but carries a different kind of payload. |
| `Unreadable` | The payload decoded but is not the shape `T` expects. |

`result.Kind` is populated whenever the tag could be read, including on a `WrongKind` failure, so the mismatch can be
explained rather than just refused.

Surrounding whitespace is ignored, because people paste with whitespace attached.

---

## Kinds

The kind is what stops a theme code being applied as a preset.

```csharp
ShareCodeHelper.Encode("myplugin.theme", theme);      // namespace it with your plugin
ShareCodeHelper.Decode<ThemeDto>(text, "myplugin.theme");
```

Read the tag without committing to a type, for deciding what a paste even is:

```csharp
if (ShareCodeHelper.TryReadKind(pastedText, out var kind))
    RouteToImporter(kind);

ShareCodeHelper.LooksLikeShareCode(clipboardText);   // shape check only, not a validity check
```

Passing an empty `expectedKind` to `Decode` accepts any kind. That is only appropriate for a tool that inspects codes
rather than applying them.

---

## Limits

```csharp
ShareCodeHelper.Limits = new ShareCodeLimits
{
    MaxEncodedCharacters = 64 * 1024,   // checked before any decoding work happens
    MaxDecodedBytes = 1024 * 1024,      // enforced during decompression
    MaxDepth = 32,                      // enforced by the JSON reader as it reads
    MaxKindBytes = 64,
};
```

**These are part of the format, not a tuning knob.** A code that needs more than the defaults to read is not a valid
share code, and every conformant reader refuses it. Raising them on your side does not make such a code portable.

Both ceilings are enforced *while* decoding rather than checked afterwards, which is the entire point:

- A payload that expands past `MaxDecodedBytes` is abandoned partway. Decompressing with no ceiling is a zip bomb: a few
  kilobytes of paste expands to gigabytes and the game dies with no useful error. Measuring afterwards means the damage
  is already done.
- Nesting past `MaxDepth` is refused by the reader before the recursion happens, because the stack overflow deep nesting
  causes is not something a `try/catch` can save you from.

---

## Format & Wire Layout

**The format is permanent from the first code a user pastes anywhere.** A change that makes an old code unreadable is a
change to the prefix, never a quiet reshuffle of the bytes.

A code is `ShareCodeHelper.Prefix` followed by URL-safe Base64 of:

```
[0]      flags       bit 0 set when the payload is deflate-compressed
[1..4]   crc32       little-endian, over the kind bytes followed by the uncompressed payload
[5]      kindLength  in UTF-8 bytes
[6..]    kind        UTF-8
[...]    payload     UTF-8 JSON, deflated when the flag says so
```

The checksum covers the payload as it will be parsed rather than as it travels, so it validates the same bytes the
deserializer sees whether or not compression was worth using.

---

## Security Notes

- **The checksum authenticates nothing.** A CRC is public and deterministic, so anyone who edits a payload can recompute
  it. What it catches is damage: a code truncated by a chat client, re-wrapped by a forum, or half-selected on the way
  to the clipboard. Nothing in a share code says who wrote it. If that matters, sign it with
  [`EncryptionHelper`](../EncryptionHelper/README.md).

- **Decode into an inert DTO, never onto a live object.** Newtonsoft runs property setters while it deserializes, so
  decoding a stranger's code straight onto a live `[AutoSave]` configuration hands them your setter side effects and
  your disk writes before you have looked at a single field. Decode into a plain data type, show the user what would
  change, and copy the fields across yourself once they agree.

- **Import is decode, preview, confirm, apply.** The preview is the security control, not a flourish: it is what turns
  "a stranger's code rewrote my presets" into "I saw what would change and said yes". Never auto-apply, and never let an
  import confirmation be remembered.

- **Type resolution is off and cannot be turned on.** `TypeNameHandling` is forced to `None` for every caller whatever
  settings they pass, so a `$type` hint in a payload is inert: it names no type and constructs nothing. Content after
  the JSON document is rejected rather than ignored, so a code that was appended to is refused.

---

## Used by

- **`NoireTheme`** ([NoireLib.UI](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/UI/README.md#theming-noiretheme)) encodes a palette under the `noire.theme` kind, decoding into an inert `ThemeSnapshot` rather than the live theme.

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EncryptionHelper](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/EncryptionHelper/README.md)
