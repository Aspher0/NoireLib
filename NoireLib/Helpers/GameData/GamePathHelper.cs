using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// The path conventions the game's archives follow, as pure string rules.<br/>
/// Nothing here opens a file: these answer "where would that file be", so a caller can resolve a reference one file
/// makes to another before deciding whether to load it.
/// </summary>
public static class GamePathHelper
{
    /// <summary>
    /// Turns a material path taken from a model into a loadable archive path.<br/>
    /// Background models store the path outright; character models store it relative, beginning with a slash, and it
    /// resolves against the folder <b>beside</b> the model's own plus a numbered variant directory.
    /// </summary>
    /// <param name="modelGamePath">Archive path of the model that referenced the material.</param>
    /// <param name="materialPath">The path as the model stores it.</param>
    /// <param name="variant">Variant directory to resolve a relative path against.</param>
    /// <returns>An absolute archive path, or null when the inputs cannot form one.</returns>
    public static string? ResolveMaterialPath(string modelGamePath, string materialPath, int variant = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelGamePath);

        if (string.IsNullOrWhiteSpace(materialPath))
            return null;

        if (!materialPath.StartsWith('/'))
            return materialPath;

        // Character models keep their materials beside the model directory rather than under it, so the model's own
        // folder is dropped before the variant folder is appended.
        var modelFolder = modelGamePath.LastIndexOf('/');
        if (modelFolder < 0)
            return null;

        var parent = modelGamePath[..modelFolder];
        var parentFolder = parent.LastIndexOf('/');
        if (parentFolder < 0)
            return null;

        return $"{parent[..parentFolder]}/material/v{variant:D4}{materialPath}";
    }

    /// <summary>
    /// Candidate archive paths for a relative material name whose file lives outside the referencing model's tree, an
    /// equipment model naming its wearer's skin material being the usual case.<br/>
    /// A character material's file name encodes its owner: <c>mt_c0201b0001_a.mtrl</c> is character <c>c0201</c> body
    /// <c>b0001</c>, which lives under the human body directory. Several candidates come back because the human
    /// directories are split on whether a variant folder exists; take the first that loads.
    /// </summary>
    /// <param name="materialPath">The relative material name, beginning with a slash.</param>
    /// <param name="variant">Variant directory for the kinds that use one.</param>
    /// <returns>The candidates in order, empty when the name is not a character material.</returns>
    public static IReadOnlyList<string> ResolveMaterialByOwnerName(string materialPath, int variant = 1)
    {
        // The grammar is /mt_c{character:4}{kind:1}{set:4}..., and anything else is not a character material.
        if (materialPath is not ['/', 'm', 't', '_', 'c', ..] || materialPath.Length < 14)
            return [];

        var character = materialPath.Substring(5, 4);
        var kind = materialPath[9];
        var set = materialPath.Substring(10, 4);

        foreach (var c in character)
        {
            if (!char.IsAsciiDigit(c))
                return [];
        }

        foreach (var c in set)
        {
            if (!char.IsAsciiDigit(c))
                return [];
        }

        var humanKind = kind switch
        {
            'b' => "body",
            'f' => "face",
            'h' => "hair",
            't' => "tail",
            'z' => "zear",
            _ => null,
        };

        if (humanKind is not null)
        {
            var owner = $"chara/human/c{character}/obj/{humanKind}/{kind}{set}";
            return [$"{owner}/material/v0001{materialPath}", $"{owner}/material{materialPath}"];
        }

        return kind switch
        {
            'e' => [$"chara/equipment/e{set}/material/v{variant:D4}{materialPath}"],
            _ => [],
        };
    }

    /// <summary>
    /// The DirectX 11 variant of a texture path, which sits beside the named file with a doubled dash prefixed to the
    /// file name.
    /// </summary>
    /// <param name="texturePath">The texture path as the material names it.</param>
    /// <returns>The variant path, or the original when the path is empty.</returns>
    public static string Dx11TexturePath(string texturePath)
    {
        if (string.IsNullOrEmpty(texturePath))
            return texturePath;

        var slash = texturePath.LastIndexOf('/');
        return slash < 0 ? $"--{texturePath}" : $"{texturePath[..(slash + 1)]}--{texturePath[(slash + 1)..]}";
    }

    /// <summary>
    /// The scene definition placed beside a background model: furniture pairs <c>.../bgparts/x.mdl</c> with
    /// <c>.../asset/x.sgb</c>.
    /// </summary>
    /// <param name="modelGamePath">The model's archive path, under <c>bgcommon/</c>.</param>
    /// <returns>The sibling scene path, or null when the path does not follow the pairing.</returns>
    public static string? SceneBesideModel(string modelGamePath)
    {
        if (string.IsNullOrWhiteSpace(modelGamePath)
            || !modelGamePath.EndsWith(".mdl", StringComparison.Ordinal)
            || !modelGamePath.Contains("/bgparts/", StringComparison.Ordinal))
            return null;

        return modelGamePath.Replace("/bgparts/", "/asset/", StringComparison.Ordinal)[..^4] + ".sgb";
    }
}
