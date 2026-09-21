using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Renames a .pap's animations so they bind to a different emote, without touching the file system. With
/// <c>removeAnimationLock</c> false, nothing changes beyond the animation names and the C009 timeline entries
/// that repeat them. A .pap the game cannot parse crashes the client. Produced bytes are always read back
/// and structurally re-parsed before being returned.
/// </summary>
public static class PapRetargeter
{
    /// <summary>
    /// Renames one animation per name in <paramref name="requiredNames"/> that <see cref="PapSharing.Match"/> can
    /// answer from <paramref name="sourcePap"/>, rewriting the animation's own name and the C009 timeline entries
    /// that repeat it together. A required name no source animation can answer is skipped, never failed.
    /// </summary>
    /// <param name="sourcePap"> The .pap's raw bytes, never modified. </param>
    /// <param name="requiredNames"> The animation names the retargeted file must declare, one per emote part. </param>
    /// <param name="removeAnimationLock">
    /// Whether to strip each renamed animation's C125 animation lock. Stripping invalidates that animation's TMB
    /// source layout (see <see cref="PapAnimationLock.Remove"/>). Its timeline is rebuilt from the parsed
    /// model, never patched in place, and its bytes no longer match the animator's original layout.
    /// </param>
    /// <param name="locksRemoved"> How many animation lock entries were removed across every renamed animation. </param>
    /// <returns> The retargeted .pap's bytes, verified to declare every name that was applied. </returns>
    /// <exception cref="InvalidDataException">
    /// The produced bytes do not read back as a valid .pap declaring every name that was applied.
    /// </exception>
    public static byte[] Retarget(byte[] sourcePap, IReadOnlyList<string> requiredNames, bool removeAnimationLock,
        out int locksRemoved)
    {
        var pap = PapFile.FromBytes(sourcePap);

        var sourceNames = pap.Animations.ConvertAll(animation => animation.GetName());
        var matches     = PapSharing.Match(sourceNames, requiredNames);
        var applied     = new List<string>();

        locksRemoved = 0;

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            var source = matches[index];
            if (source < 0)
                continue;

            var name      = requiredNames[index];
            var animation = pap.Animations[source];

            RenameAnimation(animation, name, removeAnimationLock, ref locksRemoved);
            applied.Add(name);
        }

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    /// <summary>
    /// Retargets like <see cref="Retarget"/>, with exactly one output animation per required name.<br/>
    /// A source serving several names is cloned. Unmatched sources are dropped and unanswerable names skipped.
    /// </summary>
    /// <param name="sourcePap">The .pap's bytes, never modified.</param>
    /// <param name="requiredNames">The animation names the output must declare.</param>
    /// <param name="removeAnimationLock">Whether to strip each renamed animation's C125 animation lock.</param>
    /// <param name="locksRemoved">How many animation lock entries were removed.</param>
    /// <param name="oneFrameWhenLentNames">Names clamped to one frame when their source also serves a name outside this set. Null clamps nothing.</param>
    /// <param name="clampedNames">Receives every name that took the one-frame clamp.</param>
    /// <returns>The retargeted .pap's bytes.</returns>
    /// <exception cref="InvalidDataException">The produced bytes do not read back as a valid .pap declaring every applied name.</exception>
    public static byte[] RetargetToNames(byte[] sourcePap, IReadOnlyList<string> requiredNames, bool removeAnimationLock,
        out int locksRemoved, IReadOnlyCollection<string>? oneFrameWhenLentNames = null,
        ICollection<string>? clampedNames = null)
    {
        var pap = PapFile.FromBytes(sourcePap);

        var sourceNames = pap.Animations.ConvertAll(animation => animation.GetName());

        locksRemoved = 0;

        var choices = new int[requiredNames.Count];
        var fullLengthSources = new HashSet<int>();

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            choices[index] = ChooseSourceForName(sourceNames, requiredNames[index]);

            if (choices[index] >= 0 && oneFrameWhenLentNames?.Contains(requiredNames[index]) != true)
                fullLengthSources.Add(choices[index]);
        }

        var outputs = new List<PapAnimation>(requiredNames.Count);
        var applied = new List<string>(requiredNames.Count);

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            var source = choices[index];
            if (source < 0)
                continue;

            var name = requiredNames[index];

            var animation = pap.Animations[source].Clone();

            RenameAnimation(animation, name, removeAnimationLock, ref locksRemoved);

            if (oneFrameWhenLentNames?.Contains(name) == true && fullLengthSources.Contains(source))
            {
                ClampToOneFrame(animation);
                clampedNames?.Add(name);
            }

            outputs.Add(animation);
            applied.Add(name);
        }

        pap.Animations.Clear();
        pap.Animations.AddRange(outputs);

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    /// <summary>
    /// Renames animations by their current header name.<br/>
    /// The game keys havok resources by internal name. A unique name keeps a fresh load from resolving to a resident older animation.
    /// </summary>
    /// <param name="sourcePap">The .pap's bytes, never modified.</param>
    /// <param name="renames">Current header name to new internal name.</param>
    /// <param name="keepOriginalAsAlias">Whether each renamed animation also keeps a clone under its original name.</param>
    /// <returns>The renamed .pap's bytes.</returns>
    /// <exception cref="InvalidDataException">The produced bytes do not declare an applied name.</exception>
    public static byte[] RenameInternalAnimations(byte[] sourcePap, IReadOnlyDictionary<string, string> renames,
        bool keepOriginalAsAlias = false)
    {
        var pap = PapFile.FromBytes(sourcePap);

        var applied = new List<string>();
        var aliases = new List<PapAnimation>();
        var locksRemoved = 0;

        foreach (var animation in pap.Animations)
        {
            if (!renames.TryGetValue(animation.GetName(), out var newName) || newName == animation.GetName())
                continue;

            if (keepOriginalAsAlias)
            {
                aliases.Add(animation.Clone());
                applied.Add(animation.GetName());
            }

            RenameAnimation(animation, newName, removeAnimationLock: false, ref locksRemoved);
            applied.Add(newName);
        }

        pap.Animations.AddRange(aliases);

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    // Four plain values, no string offset.
    private const string FootstepMagic = "C042";

    private static void ClampToOneFrame(PapAnimation animation)
    {
        if (animation.Tmb is not { } tmb)
            return;

        tmb.HeaderTmdh.SetLength(1);

        // Moved to frame 0 they would all fire at once.
        tmb.RemoveEntries(FootstepMagic);

        foreach (var entry in tmb.AllEntries)
        {
            entry.SetTime(0);

            switch (entry)
            {
                case Tmb.Entries.C009 body:
                    body.SetDuration(1);
                    break;

                case Tmb.Entries.C010 face:
                    face.SetDuration(1);
                    face.SetAnimationStart(0f);
                    face.SetAnimationEnd(1f);
                    break;
            }
        }
    }

    private static void RenameAnimation(PapAnimation animation, string name, bool removeAnimationLock, ref int locksRemoved)
    {
        animation.SetName(name);

        foreach (var entry in animation.Tmb?.GetAllC009Entries() ?? [])
            entry.Path.Value = name;

        if (removeAnimationLock)
            locksRemoved += PapAnimationLock.Remove(animation);
    }

    private static int ChooseSourceForName(IReadOnlyList<string> sourceNames, string name)
    {
        if (sourceNames.Count == 0)
            return -1;

        var suffix = Suffix(name);
        if (suffix.Length > 0)
        {
            for (var source = 0; source < sourceNames.Count; ++source)
            {
                if (string.Equals(Suffix(sourceNames[source]), suffix, StringComparison.OrdinalIgnoreCase))
                    return source;
            }
        }

        return 0;
    }

    private static string Suffix(string name)
    {
        var index = name.LastIndexOf('_');
        return index < 0 || index == name.Length - 1 ? string.Empty : name[(index + 1)..];
    }

    private static void Verify(byte[] result, IReadOnlyList<string> applied)
    {
        try
        {
            _ = PapFile.FromBytes(result);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The retargeted .pap failed to read back as a structurally valid .pap; refusing to return it.", ex);
        }

        var declared = PapAnimationNames.Read(result);

        foreach (var name in applied)
        {
            if (!declared.Contains(name))
                throw new InvalidDataException(
                    $"The retargeted .pap does not declare '{name}' after being read back; refusing to return it.");
        }
    }
}
